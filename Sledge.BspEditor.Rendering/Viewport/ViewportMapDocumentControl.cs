﻿using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Components;
using Sledge.BspEditor.Documents;
using Sledge.Common.Documents;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Interfaces;

namespace Sledge.BspEditor.Rendering.Viewport
{
    public class ViewportMapDocumentControl : IMapDocumentControl
    {
        private readonly Control _panel;
        private IViewport _viewport;

        private readonly List<Subscription> _subscriptions;
        private Camera _camera;

        public string Type => "MapViewport";
        public Control Control => _panel;

        public Camera Camera
        {
            get { return _camera; }
            set
            {
                _camera = value;
                if (_viewport != null) _viewport.Camera = value;
            }
        }

        public ViewportMapDocumentControl()
        {
            _camera = new PerspectiveCamera();
            _panel = new Panel {Dock = DockStyle.Fill};
            _subscriptions = new List<Subscription>
            {
                Oy.Subscribe<IDocument>("Document:Activated", DocumentActivated)
            };
        }

        private async Task DocumentActivated(IDocument doc)
        {
            var mapDoc = doc as MapDocument;

            if (mapDoc == null) return;
            if (_viewport == null)
            {
                _viewport = Renderer.Instance.Engine.CreateViewport(_camera);
                _viewport.Control.Dock = DockStyle.Fill;
                _panel.Controls.Add(_viewport.Control);
            }
        }

        public string GetSerialisedSettings()
        {
            return Camera.Serialise(Camera);
        }

        public void SetSerialisedSettings(string settings)
        {
            try
            {
                Camera = Camera.Deserialise(settings);
            }
            catch
            {
                
            }
        }

        public void Dispose()
        {
            _subscriptions.ForEach(Oy.Unsubscribe);
            _viewport?.Dispose();
        }
    }
}