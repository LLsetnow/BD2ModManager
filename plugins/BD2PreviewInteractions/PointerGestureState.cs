using System;

namespace BD2PreviewInteractions;

// One physical pointer press owns its input until release or a scene/pose reset.
internal sealed class PointerGestureState
{
    internal int? Button { get; private set; }
    internal int PointerId { get; private set; }
    internal double PressedAt { get; private set; }
    internal bool DragStarted { get; private set; }
    private bool tapConsumed;
    private bool holdCompleted;

    internal bool Begin(int button, int pointerId, double now)
    {
        if (Button != null || (button != 0 && button != 1)) return false;
        Button = button;
        PointerId = pointerId;
        PressedAt = now;
        DragStarted = tapConsumed = holdCompleted = false;
        return true;
    }

    internal bool Owns(int button, int pointerId) => Button == button && PointerId == pointerId;
    internal bool ConsumeTap()
    {
        if (Button != 0 || tapConsumed) return false;
        tapConsumed = true;
        return true;
    }

    internal bool TryStartDrag(int pointerId, double now, double holdSeconds, float distanceSquared)
    {
        if (!Owns(0, pointerId)) return false;
        if (DragStarted) return true;
        if (now - PressedAt < holdSeconds || distanceSquared < 25f) return false;
        DragStarted = true;
        return true;
    }

    internal bool CompleteHoldOnce()
    {
        if (Button != 0 || holdCompleted) return false;
        holdCompleted = true;
        return true;
    }

    internal bool End(int button, int pointerId)
    {
        if (!Owns(button, pointerId)) return false;
        Cancel();
        return true;
    }

    internal void Cancel()
    {
        Button = null;
        DragStarted = tapConsumed = holdCompleted = false;
    }
}
