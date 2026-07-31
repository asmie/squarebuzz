using Squarebuzz.App.Drawing;
using Squarebuzz.Core.Layout;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Controls;

/// <summary>Raised when the player marks a cell, so the ViewModel can apply it to the session.</summary>
public sealed class CellPaintedEventArgs(int index, CellState target) : EventArgs
{
    public int Index { get; } = index;

    public CellState Target { get; } = target;
}

/// <summary>Reports which cell the finger is over.</summary>
/// <param name="index">Row-major cell index, or -1 when the touch has ended.</param>
/// <param name="isInLeftHalf">True when the cell is in the left half of the grid.</param>
public sealed class TouchedCellEventArgs(int index, bool isInLeftHalf) : EventArgs
{
    public int Index { get; } = index;

    public bool IsInLeftHalf { get; } = isInLeftHalf;
}

/// <summary>
/// The interactive board: a single <see cref="GraphicsView"/> that draws every cell and turns
/// touches into paint requests.
/// </summary>
/// <remarks>
/// Reproduces the prototype's drag semantics exactly, because they are what make the board feel
/// right on a phone. The <em>first</em> cell touched decides the target value by toggling; the
/// drag then paints that same value into each newly entered cell. A <c>seen</c> set stops a
/// wobbling finger from flipping a cell back and forth as it re-enters it.
/// </remarks>
public sealed class BoardView : GraphicsView
{
    /// <summary>Long-press duration that turns a tap into a cross, in hold-to-cross mode.</summary>
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(480);

    private readonly BoardDrawable _drawable = new();
    private readonly HashSet<int> _paintedThisDrag = [];

    private CellState _dragTarget = CellState.Empty;
    private bool _isDragging;
    private CancellationTokenSource? _longPressCancellation;
    private int _pressedIndex = -1;

    public BoardView()
    {
        Drawable = _drawable;

        StartInteraction += OnStartInteraction;
        DragInteraction += OnDragInteraction;
        EndInteraction += OnEndInteraction;
        CancelInteraction += OnCancelInteraction;
    }

    public event EventHandler<CellPaintedEventArgs>? CellPainted;

    /// <summary>
    /// Raised as the finger moves between cells, and once with -1 on release. Drives the
    /// magnifier; <see cref="TouchedCellEventArgs.IsInLeftHalf"/> lets the host park the panel
    /// on the opposite side of the board from the hand.
    /// </summary>
    public event EventHandler<TouchedCellEventArgs>? TouchedCellChanged;

    /// <summary>Fired when a long press asks for a cross, so the host can give haptic feedback.</summary>
    public event EventHandler? CrossGestureRecognised;

    public static readonly BindableProperty SessionProperty = BindableProperty.Create(
        nameof(Session),
        typeof(GameSession),
        typeof(BoardView),
        propertyChanged: (bindable, _, _) =>
        {
            var view = (BoardView)bindable;

            // A different game's strikes are not news - they seed silently on first draw.
            view._drawable.ResetStrikeAnimations();
            view.Refresh();
        });

    public GameSession? Session
    {
        get => (GameSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public static readonly BindableProperty ZoomPercentProperty = BindableProperty.Create(
        nameof(ZoomPercent),
        typeof(int),
        typeof(BoardView),
        GameSettings.DefaultCellZoomPercent,
        propertyChanged: (bindable, _, _) => ((BoardView)bindable).Refresh());

    public int ZoomPercent
    {
        get => (int)GetValue(ZoomPercentProperty);
        set => SetValue(ZoomPercentProperty, value);
    }

    public static readonly BindableProperty BigNumbersProperty = BindableProperty.Create(
        nameof(BigNumbers),
        typeof(bool),
        typeof(BoardView),
        false,
        propertyChanged: (bindable, _, _) => ((BoardView)bindable).Refresh());

    public bool BigNumbers
    {
        get => (bool)GetValue(BigNumbersProperty);
        set => SetValue(BigNumbersProperty, value);
    }

    public static readonly BindableProperty TapBehaviourProperty = BindableProperty.Create(
        nameof(TapBehaviour),
        typeof(TapBehaviour),
        typeof(BoardView),
        Core.Model.TapBehaviour.ModeButton);

    public TapBehaviour TapBehaviour
    {
        get => (TapBehaviour)GetValue(TapBehaviourProperty);
        set => SetValue(TapBehaviourProperty, value);
    }

    /// <summary>
    /// Highlights a hinted cell until the next refresh clears it, announcing itself with two
    /// quick pulses of the gold ring - a static ring is easy to miss on a busy board.
    /// </summary>
    public void ShowHint(int index)
    {
        _drawable.HintIndex = index;
        _drawable.HintRingWidth = 3f;
        Invalidate();

        if (Services.MotionPreferences.ReduceMotion)
        {
            return;
        }

        this.AbortAnimation("hintPulse");

        // Two pulses of 0 -> 6 -> 0, then the resting 3, per the design's motion spec.
        new Animation(
            v =>
            {
                _drawable.HintRingWidth = (float)(6 * Math.Abs(Math.Sin(v * Math.PI * 2)));
                Invalidate();
            },
            0,
            1)
            .Commit(
                this,
                "hintPulse",
                length: 900,
                finished: (_, _) =>
                {
                    _drawable.HintRingWidth = 3f;
                    Invalidate();
                });
    }

    /// <summary>
    /// Pops the cell that was just marked: small, overshoot, settle. What makes a fill feel
    /// placed rather than switched on.
    /// </summary>
    public void PopCell(int index)
    {
        if (Services.MotionPreferences.ReduceMotion)
        {
            return;
        }

        this.AbortAnimation("cellPop");

        _drawable.PopIndex = index;

        // SpringOut overshoots past 1 before settling, which is the 40% -> 114% -> 100% curve
        // the design asks for without hand-writing the keyframes.
        new Animation(
            v =>
            {
                _drawable.PopScale = (float)v;
                Invalidate();
            },
            0.4,
            1.0,
            Easing.SpringOut)
            .Commit(
                this,
                "cellPop",
                length: 160,
                finished: (_, _) =>
                {
                    _drawable.PopIndex = -1;
                    _drawable.PopScale = 1f;
                    Invalidate();
                });
    }

    /// <summary>Flashes a cell to show the fill was wrong.</summary>
    public async Task FlashMistakeAsync(int index)
    {
        _drawable.MistakeIndex = index;
        Invalidate();

        await Task.Delay(520);

        _drawable.MistakeIndex = -1;
        Invalidate();
    }

    public static readonly BindableProperty AvailableSizeProperty = BindableProperty.Create(
        nameof(AvailableSize),
        typeof(Size),
        typeof(BoardView),
        Size.Zero,
        propertyChanged: (bindable, _, _) => ((BoardView)bindable).Refresh());

    /// <summary>
    /// Space the host is willing to give the board.
    /// </summary>
    /// <remarks>
    /// Supplied by the parent rather than read from this view's own <c>Width</c>. Sizing from
    /// its own size cannot work: the view's size comes from the WidthRequest that Refresh sets,
    /// so it would feed on its own output and stay stuck at whatever the first unmeasured pass
    /// produced - which is the minimum cell size.
    /// </remarks>
    public Size AvailableSize
    {
        get => (Size)GetValue(AvailableSizeProperty);
        set => SetValue(AvailableSizeProperty, value);
    }

    /// <summary>
    /// The geometry the board was last drawn with, so an overlay can line up with the cells.
    /// </summary>
    public BoardLayout CurrentLayout => _drawable.Layout;

    /// <summary>Raised after <see cref="Refresh"/> recomputes the geometry.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Recomputes layout for the available space and redraws.</summary>
    public void Refresh()
    {
        if (Session is not { } session)
        {
            return;
        }

        // Fall back to the view's own size only before the host has reported one.
        var width = AvailableSize.Width > 0 ? AvailableSize.Width : Width;
        var height = AvailableSize.Height > 0 ? AvailableSize.Height : Height;

        var layout = BoardLayout.Calculate(session.Puzzle, width, height, ZoomPercent);

        _drawable.Puzzle = session.Puzzle;
        _drawable.Cells = session.Cells.ToArray();
        _drawable.Layout = layout;
        _drawable.BigNumbers = BigNumbers;
        _drawable.Palette = BoardPalette.FromResources();

        // The board is centred when it fits and pinned top-left when it does not, so a
        // scrolled board never hides its gutters.
        HeightRequest = layout.TotalHeight;
        WidthRequest = layout.TotalWidth;

        Invalidate();

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Redraws from the session without recomputing layout - the common case after a move.</summary>
    public void RefreshCells()
    {
        if (Session is not { } session)
        {
            return;
        }

        _drawable.Cells = session.Cells.ToArray();
        Invalidate();

        // A move can newly satisfy a clue, whose strike wipes in over a few frames. The drawable
        // computes the wipe from wall time and only learns of a new strike *during* the next
        // draw - too late for this method to ask whether one appeared - so every move simply
        // buys a wipe's worth of redraws. A dozen frames of a cheap draw is nothing next to
        // drag-painting, which already redraws per entered cell.
        if (!Services.MotionPreferences.ReduceMotion && !this.AnimationIsRunning("strikeWipe"))
        {
            new Animation(_ => Invalidate(), 0, 1)
                .Commit(this, "strikeWipe", length: 240, finished: (_, _) => Invalidate());
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        // Only relevant before the host has supplied AvailableSize; afterwards the layout is
        // driven entirely by that, and recomputing here would reintroduce the feedback loop.
        if (AvailableSize.Width <= 0)
        {
            Refresh();
        }
    }

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (Session is not { } session || e.Touches.Length == 0)
        {
            return;
        }

        var index = _drawable.Layout.HitTest(e.Touches[0].X, e.Touches[0].Y);

        if (index is not { } cell)
        {
            return;
        }

        _pressedIndex = cell;
        _paintedThisDrag.Clear();

        // The first cell decides what the whole drag will paint, by toggling its own value.
        var current = session[cell];
        _dragTarget = session.Mode == PaintMode.Fill
            ? current == CellState.Filled ? CellState.Empty : CellState.Filled
            : current == CellState.Crossed ? CellState.Empty : CellState.Crossed;

        _isDragging = true;

        Highlight(cell);
        Paint(cell);

        if (TapBehaviour == Core.Model.TapBehaviour.HoldToCross)
        {
            StartLongPressTimer(cell);
        }
    }

    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        if (!_isDragging || Session is null || e.Touches.Length == 0)
        {
            return;
        }

        var index = _drawable.Layout.HitTest(e.Touches[0].X, e.Touches[0].Y);

        if (index is not { } cell)
        {
            return;
        }

        if (cell != _pressedIndex)
        {
            // Moving off the pressed cell means this is a drag, not a hold.
            CancelLongPress();
            _pressedIndex = cell;
        }

        Highlight(cell);
        Paint(cell);
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        CancelLongPress();
        EndDrag();
    }

    private void OnCancelInteraction(object? sender, EventArgs e)
    {
        CancelLongPress();
        EndDrag();
    }

    private void EndDrag()
    {
        _isDragging = false;
        _paintedThisDrag.Clear();

        _drawable.HighlightRow = -1;
        _drawable.HighlightColumn = -1;
        Invalidate();

        TouchedCellChanged?.Invoke(this, new TouchedCellEventArgs(-1, isInLeftHalf: false));
    }

    private void Paint(int index)
    {
        // Each cell is painted at most once per drag, so re-entering it does not toggle it back.
        if (!_paintedThisDrag.Add(index))
        {
            return;
        }

        CellPainted?.Invoke(this, new CellPaintedEventArgs(index, _dragTarget));
    }

    private void Highlight(int index)
    {
        var layout = _drawable.Layout;

        if (layout.Columns == 0)
        {
            return;
        }

        var column = index % layout.Columns;

        _drawable.HighlightRow = index / layout.Columns;
        _drawable.HighlightColumn = column;
        _drawable.HintIndex = -1;

        TouchedCellChanged?.Invoke(this, new TouchedCellEventArgs(index, column < layout.Columns / 2));
    }

    private void StartLongPressTimer(int index)
    {
        CancelLongPress();

        var cancellation = new CancellationTokenSource();
        _longPressCancellation = cancellation;

        // Taken here, on the UI thread, while the source is certainly alive. The task below must
        // only ever touch this token, never the source: a quick tap cancels *and disposes* the
        // source from OnEndInteraction, possibly before the pool has even started the task, and
        // a disposed source throws from its Token property. The token itself stays valid.
        var token = cancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(LongPressDelay, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // The finger stayed put: convert the gesture into a cross and stop the drag so
            // the release does not also paint.
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (token.IsCancellationRequested || Session is not { } session)
                {
                    return;
                }

                // Stopping the drag here also stops the release from painting again.
                _isDragging = false;

                var target = session[index] == CellState.Crossed ? CellState.Empty : CellState.Crossed;
                CellPainted?.Invoke(this, new CellPaintedEventArgs(index, target));
                CrossGestureRecognised?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    private void CancelLongPress()
    {
        _longPressCancellation?.Cancel();
        _longPressCancellation?.Dispose();
        _longPressCancellation = null;
    }
}
