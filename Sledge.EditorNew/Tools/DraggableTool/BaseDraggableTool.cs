using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Input;
using Sledge.DataStructures.Geometric;
using Sledge.EditorNew.UI.Viewports;
using Sledge.Gui.Structures;

namespace Sledge.EditorNew.Tools.DraggableTool
{
    public abstract class BaseDraggableTool : BaseTool
    {
        public List<IDraggableState> States { get; set; }

        public IDraggable CurrentDraggable { get; private set; }
        private ViewportEvent _lastDragMoveEvent = null;
        private Coordinate _lastDragPoint = null;

        protected BaseDraggableTool()
        {
            States = new List<IDraggableState>();
        }

        #region Virtual events
        protected virtual void OnDraggableClicked(IViewport2D viewport, ViewportEvent e, Coordinate position, IDraggable draggable)
        {

        }

        protected virtual void OnDraggableDragStarted(IViewport2D viewport, ViewportEvent e, Coordinate position, IDraggable draggable)
        {

        }

        protected virtual void OnDraggableDragMoved(IViewport2D viewport, ViewportEvent e, Coordinate previousPosition, Coordinate position, IDraggable draggable)
        {

        }

        protected virtual void OnDraggableDragEnded(IViewport2D viewport, ViewportEvent e, Coordinate position, IDraggable draggable)
        {

        }
        #endregion

        public override void MouseClick(IMapViewport viewport, ViewportEvent e)
        {
            if (!viewport.Is2D || e.Dragging || e.Button != MouseButton.Left) return;
            var vp = (IViewport2D)viewport;
            if (CurrentDraggable == null) return;
            var point = viewport.ScreenToWorld(e.X, viewport.Height - e.Y);
            OnDraggableClicked(vp, e, point, CurrentDraggable);
            if (!e.Handled) CurrentDraggable.Click(vp, e, point);
        }

        public override void MouseMove(IMapViewport viewport, ViewportEvent e)
        {
            if (!viewport.Is2D || e.Dragging || e.Button == MouseButton.Left) return;
            var vp = (IViewport2D)viewport;
            var point = viewport.ScreenToWorld(e.X, viewport.Height - e.Y);
            IDraggable drag = null;
            foreach (var state in States)
            {
                var drags = state.GetDraggables(vp).ToList();
                drags.Add(state);
                foreach (var draggable in drags)
                {
                    if (draggable.CanDrag(vp, e, point))
                    {
                        drag = draggable;
                        break;
                    }
                }
                if (drag != null) break;
            }
            if (drag != CurrentDraggable)
            {
                if (CurrentDraggable != null) CurrentDraggable.Unhighlight(vp);
                CurrentDraggable = drag;
                if (CurrentDraggable != null) CurrentDraggable.Highlight(vp);
            }
        }
        
        public override void DragStart(IMapViewport viewport, ViewportEvent e)
        {
            if (!viewport.Is2D || e.Button != MouseButton.Left) return;
            var vp = (IViewport2D)viewport;
            if (CurrentDraggable == null) return;
            _lastDragPoint = viewport.ScreenToWorld(e.X, viewport.Height - e.Y);
            OnDraggableDragStarted(vp, e, _lastDragPoint, CurrentDraggable);
            if (!e.Handled) CurrentDraggable.StartDrag(vp, e, _lastDragPoint);
            _lastDragMoveEvent = e;
        }

        public override void DragMove(IMapViewport viewport, ViewportEvent e)
        {
            if (!viewport.Is2D || e.Button != MouseButton.Left) return;
            var vp = (IViewport2D)viewport;
            if (CurrentDraggable == null) return;
            var point = viewport.ScreenToWorld(e.X, viewport.Height - e.Y);
            OnDraggableDragMoved(vp, e, _lastDragPoint, point, CurrentDraggable);
            if (!e.Handled) CurrentDraggable.Drag(vp, e, _lastDragPoint, point);
            _lastDragPoint = point;
            _lastDragMoveEvent = e;
        }

        public override void DragEnd(IMapViewport viewport, ViewportEvent e)
        {
            if (!viewport.Is2D || e.Button != MouseButton.Left) return;
            var vp = (IViewport2D)viewport;
            if (CurrentDraggable == null) return;
            var point = viewport.ScreenToWorld(e.X, viewport.Height - e.Y);
            OnDraggableDragEnded(vp, e, point, CurrentDraggable);
            if (!e.Handled) CurrentDraggable.EndDrag(vp, e, point);
            _lastDragMoveEvent = null;
            _lastDragPoint = null;
        }

        public override void PositionChanged(IMapViewport viewport, ViewportEvent e)
        {
            if (viewport.Is2D && _lastDragMoveEvent != null && CurrentDraggable != null && _lastDragMoveEvent.Sender == viewport)
            {
                var vp = (IViewport2D) viewport;
                var point = viewport.ScreenToWorld(_lastDragMoveEvent.X, viewport.Height - _lastDragMoveEvent.Y);
                var ev = new ViewportEvent(viewport)
                {
                    Dragging = true,
                    Button = _lastDragMoveEvent.Button,
                    StartX = _lastDragMoveEvent.StartX,
                    StartY = _lastDragMoveEvent.StartY
                };
                ev.X = ev.LastX = _lastDragMoveEvent.X;
                ev.Y = ev.LastY = _lastDragMoveEvent.Y;
                OnDraggableDragMoved(vp, ev, _lastDragPoint, point, CurrentDraggable);
                if (!ev.Handled) CurrentDraggable.Drag(vp, ev, _lastDragPoint, point);
                _lastDragPoint = point;
            }
            base.PositionChanged(viewport, e);
        }

        public override void Render(IMapViewport viewport)
        {
            if (!viewport.Is2D) return;
            var vp = (IViewport2D) viewport;
            var foundActive = false;
            foreach (var state in States)
            {
                foreach (var draggable in state.GetDraggables(vp))
                {
                    if (draggable == CurrentDraggable) foundActive = true;
                    else draggable.Render(vp);
                }
                if (state == CurrentDraggable) foundActive = true;
                else state.Render(vp);
            }
            if (CurrentDraggable != null && foundActive) CurrentDraggable.Render(vp);
        }

        protected bool GetSelectionBox(BoxState state, out Box boundingbox)
        {
            // If one of the dimensions has a depth value of 0, extend it out into infinite space
            // If two or more dimensions have depth 0, do nothing.

            var sameX = state.Start.X == state.End.X;
            var sameY = state.Start.Y == state.End.Y;
            var sameZ = state.Start.Z == state.End.Z;
            var start = state.Start.Clone();
            var end = state.End.Clone();
            var invalid = false;

            if (sameX)
            {
                if (sameY || sameZ) invalid = true;
                start.X = Decimal.MinValue;
                end.X = Decimal.MaxValue;
            }

            if (sameY)
            {
                if (sameZ) invalid = true;
                start.Y = Decimal.MinValue;
                end.Y = Decimal.MaxValue;
            }

            if (sameZ)
            {
                start.Z = Decimal.MinValue;
                end.Z = Decimal.MaxValue;
            }

            boundingbox = new Box(start, end);
            return !invalid;
        }

        #region Unused (for now)
        public override void MouseDown(IMapViewport viewport, ViewportEvent e)
        {

        }

        public override void MouseUp(IMapViewport viewport, ViewportEvent e)
        {

        }

        public override void MouseEnter(IMapViewport viewport, ViewportEvent e)
        {

        }

        public override void MouseLeave(IMapViewport viewport, ViewportEvent e)
        {

        }

        public override void MouseDoubleClick(IMapViewport viewport, ViewportEvent e)
        {

        }

        public override void MouseWheel(IMapViewport viewport, ViewportEvent e)
        {

        }

        public override void KeyPress(IMapViewport viewport, ViewportEvent e)
        {

        }

        public override void KeyDown(IMapViewport viewport, ViewportEvent e)
        {

        }

        public override void KeyUp(IMapViewport viewport, ViewportEvent e)
        {

        }

        public override void UpdateFrame(IMapViewport viewport, Frame frame)
        {

        }

        public override void PreRender(IMapViewport viewport)
        {

        }
        #endregion
    }
}