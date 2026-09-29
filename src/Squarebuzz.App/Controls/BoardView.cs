using Microsoft.Extensions.DependencyInjection;
using Squarebuzz.App.Drawing;
using Squarebuzz.Core.Layout;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Controls;

/// <summary>Raised when the player marks a cell, so the ViewModel can apply it to the session.</summary>
public sealed class CellPaintedEventArgs(int index, CellState target, bool continuesStroke = false) : EventArgs
{
    public int Index { get; } = index;

    public CellState Target { get; } = target;

    /// <summary>True for every square of a drag after the first. See GameSession.Paint.</summary>
    public bool ContinuesStroke { get; } = continuesStroke;
}

/// <summary>Reports which cell the finger is over.</summary>
/// <param name="index">Row-major cell index, or -1 when the touch has ended.</param>
/// <param name="isInLeftHalf">True when the cell is in the left half of the grid.</param>
public sealed class TouchedCellEventArgs(int index, bool isInLeftHalf) : EventArgs
{
    public int Index { get; } = index;

    public bool IsInLeftHalf { get; } = isInLeftHalf;
}

/// <summary>Draws the board and converts touch gestures into paint requests.</summary>
/// <remarks>
/// The first touched cell determines the drag value. Each cell is visited at most once per stroke.
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
    private int _mistakeGeneration;
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

    /// <summary>The session <see cref="LayoutChanged"/> was last raised for.</summary>
    private GameSession? _layoutSession;

    public BoardView()
    {
        Drawable = _drawable;

        _drawable.WipeInProgress += OnWipeInProgress;

        StartInteraction += OnStartInteraction;
        DragInteraction += OnDragInteraction;
        EndInteraction += OnEndInteraction;
        CancelInteraction += OnCancelInteraction;
    }

    /// <summary>
    /// Ends any gesture in flight while the native view still exists, so the scrollers it
    /// claimed are handed back and a pending long press cannot fire into a detached view.
    /// </summary>
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.OldHandler is not null)
        {
            CancelLongPress();
            EndDrag();
        }

        base.OnHandlerChanging(args);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler?.MauiContext?.Services is not { } services)
        {
            return;
        }

        // Resolve once per handler, not on every draw. Android needs the actual asset name;
        // Apple needs the registered native face, and Windows also needs the family fragment.
#if WINDOWS
        var name = services.GetRequiredService<IFontManager>()
            .GetFontFamily(Microsoft.Maui.Font.OfSize("BodyBold", 12)).Source;
#else
        var name = services.GetRequiredService<IFontRegistrar>().GetFont("BodyBold");
#endif
        _drawable.ClueFont = string.IsNullOrEmpty(name)
            ? Microsoft.Maui.Graphics.Font.DefaultBold
            : new Microsoft.Maui.Graphics.Font(name, FontWeights.Bold);
        Invalidate();
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

            // Seed the new board's existing clue strikes without animation.
            view._drawable.ResetStrikeAnimations();

            // A new game must not inherit the previous one's hint ring; bumping the
            // generation also defuses any pending clear timer.
            view._hintGeneration++;
            view._drawable.HintIndex = -1;

            // Clear the previous board's mistake highlight.
            view._mistakeGeneration++;
            view._drawable.MistakeIndex = -1;

            // Nor any part of a gesture on the board that has just gone away: a pending long
            // press would otherwise mature and cross a cell of the new game, and the scrollers
            // it claimed would stay frozen.
            view.CancelLongPress();
            view.EndDrag();

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

    /// <summary>Highlights a hinted cell for HintDisplayDuration.</summary>
    /// <remarks>
    /// The session has already applied the hint. A timer clears the ring independently of later touches.
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

    /// <summary>Animates a newly filled cell with a short scale overshoot.</summary>
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

    /// <summary>How long the warn tint stays on a wrongly filled cell.</summary>
    private static readonly TimeSpan MistakeFlashDuration = TimeSpan.FromMilliseconds(520);

    /// <summary>Flashes an incorrect fill. Only the latest flash timer may clear the highlight.</summary>
    public void FlashMistake(int index)
    {
        _drawable.MistakeIndex = index;
        Invalidate();

        var generation = ++_mistakeGeneration;

        Dispatcher.DispatchDelayed(MistakeFlashDuration, () =>
        {
            if (generation != _mistakeGeneration)
            {
                return;
            }

            _drawable.MistakeIndex = -1;
            Invalidate();
        });
    }

    public static readonly BindableProperty AvailableSizeProperty = BindableProperty.Create(
        nameof(AvailableSize),
        typeof(Size),
        typeof(BoardView),
        Size.Zero,
        propertyChanged: (bindable, _, _) => ((BoardView)bindable).Refresh());

    /// <summary>Available board size supplied by the parent.</summary>
    /// <remarks>
    /// Using this control's own requested size would create a layout feedback loop.
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

    /// <summary>
    /// Raised after <see cref="Refresh"/> when the geometry or the puzzle actually changed.
    /// </summary>
    /// <remarks>
    /// Not on every refresh: a theme or palette change refreshes too, and the game page answers
    /// this event by rebuilding up to 625 screen-reader cells - which also threw away the
    /// screen reader's place on the board, for a change that moved nothing.
    /// </remarks>
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
        // A new session counts even on an identical board: restarting a picture reuses its
        // Puzzle, and the overlay has to be rebuilt for the game now being played.
        var geometryChanged = layout != _drawable.Layout || !ReferenceEquals(session, _layoutSession);
        _layoutSession = session;

        _drawable.Puzzle = session.Puzzle;
        _drawable.SetCells(session.Cells);
        _drawable.Layout = layout;
        _drawable.Palette = BoardPalette.FromResources();

        // The board is centred when it fits and pinned top-left when it does not, so a
        // scrolled board never hides its gutters.
        HeightRequest = layout.TotalHeight;
        WidthRequest = layout.TotalWidth;

        Invalidate();

        if (geometryChanged)
        {
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Redraws from the session without recomputing layout - the common case after a move.</summary>
    public void RefreshCells()
    {
        if (Session is not { } session)
        {
            return;
        }

        _drawable.SetCells(session.Cells);

        // Invalidate once. An active clue-strike animation requests additional frames as needed.
        Invalidate();
    }

    /// <summary>Schedules frames while a clue-strike animation is active.</summary>
    /// <remarks>
    /// Dispatch the invalidation after Draw returns to avoid drawing re-entry.
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

        // Capture board gestures so the scroll host does not intercept a paint drag.
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

        // Defer the first mark until release, drag or hold detection. Painting immediately would
        // charge a wrong-fill mistake before a valid cross gesture was recognized.
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

        // Ignore repeated touch samples in the same cell before updating painting or magnification.
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

        // The first square painted starts the stroke; everything after continues it.
        var continuesStroke = _paintedThisDrag.Count > 1;

        CellPainted?.Invoke(this, new CellPaintedEventArgs(index, _dragTarget, continuesStroke));
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
