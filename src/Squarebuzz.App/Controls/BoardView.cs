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
public sealed partial class BoardView : GraphicsView
{
    /// <summary>Long-press duration that turns a tap into a cross, in hold-to-cross mode.</summary>
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(480);

    /// <summary>
    /// How long the hint ring stays on the board. Public so the page can keep its own hint
    /// affordances (dimmed overlays) in step with the ring.
    /// </summary>
    public static readonly TimeSpan HintDisplayDuration = TimeSpan.FromSeconds(4);

    private readonly BoardDrawable _drawable = new();
    private readonly HashSet<int> _paintedThisDrag = [];

    private CellState _dragTarget = CellState.Empty;
    private int _hintGeneration;
    private bool _isDragging;
    private CancellationTokenSource? _longPressCancellation;
    private int _pressedIndex = -1;
    private bool _wipeFrameQueued;

    /// <summary>
    /// The mark hold-to-cross is holding back, or -1 when nothing is pending.
    /// </summary>
    /// <remarks>
    /// Only that mode defers a mark. See <see cref="OnStartInteraction"/> for why it must.
    /// </remarks>
    private int _deferredPaintIndex = -1;

    public BoardView()
    {
        Drawable = _drawable;

        _drawable.WipeInProgress += OnWipeInProgress;

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

            // A new game must not inherit the previous one's hint ring; bumping the
            // generation also defuses any pending clear timer.
            view._hintGeneration++;
            view._drawable.HintIndex = -1;

            // Nor a mark still pending from a gesture on the board that has just gone away.
            view._deferredPaintIndex = -1;

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
    /// Highlights a hinted cell for <see cref="HintDisplayDuration"/>, announcing itself with
    /// two quick pulses of the gold ring - a static ring is easy to miss on a busy board.
    /// </summary>
    /// <remarks>
    /// The hinted mark is already applied to the board by the session, so the ring is pure
    /// attention direction. It clears itself on a timer rather than on the next touch: a player
    /// reaching for the board must not wipe the very thing they paid a hint to see.
    /// </remarks>
    public void ShowHint(int index)
    {
        _drawable.HintIndex = index;
        _drawable.HintRingWidth = 3f;
        Invalidate();

        // The generation guard keeps a stale timer from wiping a newer hint's ring.
        var generation = ++_hintGeneration;
        Dispatcher.DispatchDelayed(HintDisplayDuration, () =>
        {
            if (generation != _hintGeneration)
            {
                return;
            }

            _drawable.HintIndex = -1;
            _drawable.HintRingWidth = 3f;
            Invalidate();
        });

        if (Services.MotionPreferences.ReduceMotion)
        {
            return;
        }

        this.AbortAnimation("hintPulse");

        // Two pulses of 2 -> 6 -> 2, then the resting 3, per the design's motion spec. The
        // floor of 2 keeps the ring visible for the whole pulse - a |sin| that dips to zero
        // makes the ring blink out entirely, including on the very first frame.
        new Animation(
            v =>
            {
                _drawable.HintRingWidth = 2f + (float)(4 * Math.Abs(Math.Sin(v * Math.PI * 2)));
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

        var layout = BoardLayout.Calculate(session.Puzzle, width, height, ZoomPercent, BigNumbers);

        _drawable.Puzzle = session.Puzzle;
        _drawable.Cells = session.Cells.ToArray();
        _drawable.Layout = layout;
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

        // One frame. If the move newly satisfied a clue, the draw will notice its strike is
        // mid-wipe and ask for the next frame itself through OnWipeInProgress - so the extra
        // frames happen when there is genuinely something moving, not after every cell.
        Invalidate();
    }

    /// <summary>
    /// Keeps a clue strike's wipe going, one frame at a time, for exactly as long as the drawable
    /// reports it unfinished.
    /// </summary>
    /// <remarks>
    /// Dispatched rather than invalidated straight away: this arrives from inside
    /// <see cref="BoardDrawable.Draw"/>, and asking a view to redraw part-way through its own
    /// draw is how platforms produce a dropped frame or a re-entrancy assert.
    /// </remarks>
    private void OnWipeInProgress(object? sender, EventArgs e)
    {
        if (_wipeFrameQueued)
        {
            return;
        }

        _wipeFrameQueued = true;

        Dispatcher.Dispatch(() =>
        {
            _wipeFrameQueued = false;
            Invalidate();
        });
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
            // A gutter touch is not a paint gesture, so it is left to the scrolling host - that
            // is how a board bigger than the screen gets panned.
            return;
        }

        // From here the gesture is the board's. Without this the scrolling host takes it back
        // the moment the finger travels past the system touch slop, which is a fraction of one
        // square: a drag across ten cells used to paint two and then be cancelled.
        ClaimGestureFromScrollers(true);

        _pressedIndex = cell;
        _paintedThisDrag.Clear();

        // The first cell decides what the whole drag will paint, by toggling its own value.
        var current = session[cell];
        _dragTarget = session.Mode == PaintMode.Fill
            ? current == CellState.Filled ? CellState.Empty : CellState.Filled
            : current == CellState.Crossed ? CellState.Empty : CellState.Crossed;

        _isDragging = true;

        Highlight(cell);

        // Hold-to-cross holds the first mark back until the gesture has declared itself.
        //
        // Painting on touch-down and *then* crossing once the hold matured meant every cross was
        // preceded by a fill. On a cell that is not part of the picture that fill is refused as a
        // mistake, so the cross gesture - whose whole purpose is marking cells that stay blank -
        // charged the player a mistake, a board shake and an "Oops" every single time, and two
        // uses cost them a star. Deferring costs a tap the 480 ms it takes to prove it is not a
        // hold, which is the price of the mode and not a bug.
        if (TapBehaviour == Core.Model.TapBehaviour.HoldToCross)
        {
            _deferredPaintIndex = cell;
            StartLongPressTimer(cell);
            return;
        }

        Paint(cell);
    }

    /// <summary>
    /// Commits the mark <see cref="_deferredPaintIndex"/> was holding, if any.
    /// </summary>
    /// <remarks>
    /// Called from the two routes that prove a press was not a hold - the finger moved to another
    /// cell, or it lifted. Cancellation deliberately does not call this: a gesture the platform
    /// took away never became a tap.
    /// </remarks>
    private void FlushDeferredPaint()
    {
        if (_deferredPaintIndex < 0)
        {
            return;
        }

        var index = _deferredPaintIndex;
        _deferredPaintIndex = -1;

        Paint(index);
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

        // A finger crossing one cell produces a touch sample per frame, and everything below is
        // per-cell work: the crosshair, the magnifier notification, and the paint request that
        // ends in a full session update. Samples that did not change cell are dropped here
        // rather than deduplicated three layers down.
        if (cell == _pressedIndex)
        {
            return;
        }

        // Moving off the pressed cell means this is a drag, not a hold - so the mark the hold was
        // holding back is committed first, and the stroke starts where the finger went down.
        CancelLongPress();
        FlushDeferredPaint();

        _pressedIndex = cell;

        Highlight(cell);
        Paint(cell);
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        CancelLongPress();

        // Lifted before the hold matured, so it was a tap after all: the mark lands now.
        FlushDeferredPaint();

        EndDrag();
    }

    private void OnCancelInteraction(object? sender, EventArgs e)
    {
        CancelLongPress();

        // Not flushed: the platform took the gesture away, so it never became a tap.
        _deferredPaintIndex = -1;

        EndDrag();
    }

    /// <summary>
    /// Platform hook: takes the gesture away from any scrolling ancestor, or gives it back.
    /// Does nothing where the platform has no such notion.
    /// </summary>
    partial void ClaimGestureFromScrollers(bool claim);

    private void EndDrag()
    {
        // Released on every route out of a gesture, including cancellation - a host left
        // permanently unable to intercept would stop scrolling altogether.
        ClaimGestureFromScrollers(false);

        _isDragging = false;
        _paintedThisDrag.Clear();
        _deferredPaintIndex = -1;

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
        var row = index / layout.Columns;

        if (_drawable.HighlightRow != row || _drawable.HighlightColumn != column)
        {
            _drawable.HighlightRow = row;
            _drawable.HighlightColumn = column;

            // Redrawn here rather than left to the paint that follows: dragging back over a cell
            // already painted in this drag makes no move at all, and the crosshair still has to
            // keep up with the finger.
            Invalidate();
        }

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

                // The hold matured, so the fill it was holding back is abandoned rather than
                // committed - this gesture was always going to be a cross.
                _deferredPaintIndex = -1;

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
