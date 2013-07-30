using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTK;
using Sledge.Common.Mediator;
using Sledge.DataStructures.GameData;
using Sledge.DataStructures.Geometric;
using Sledge.DataStructures.MapObjects;
using Sledge.Editor.Actions;
using Sledge.Editor.Editing;
using Sledge.Editor.History;
using Sledge.Editor.Rendering;
using Sledge.Editor.Rendering.Helpers;
using Sledge.Editor.Tools;
using Sledge.Editor.UI;
using Sledge.Editor.Visgroups;
using Sledge.Graphics;
using Sledge.Graphics.Helpers;
using Sledge.Providers;
using Sledge.Providers.GameData;
using Sledge.Providers.Map;
using Sledge.Providers.Texture;
using Sledge.Settings;
using Sledge.Settings.Models;
using Sledge.UI;
using Path = System.IO.Path;

namespace Sledge.Editor.Documents
{
    public class Document
    {
        public string MapFile { get; set; }
        public Map Map { get; set; }

        public Game Game { get; set; }
        public GameData GameData { get; set; }

        public Pointfile Pointfile { get; set; }

        public RenderManager Renderer { get; private set; }

        public SelectionManager Selection { get; private set; }
        public HistoryManager History { get; private set; }
        public HelperManager HelperManager { get; set; }

        private readonly DocumentSubscriptions _subscriptions;

        private Document()
        {
            Selection = new SelectionManager(this);
            History = new HistoryManager(this);
            HelperManager = new HelperManager(this);
        }

        public Document(string mapFile, Map map, Game game)
        {
            MapFile = mapFile;
            Map = map;
            Game = game;

            _subscriptions = new DocumentSubscriptions(this);

            Selection = new SelectionManager(this);
            History = new HistoryManager(this);
            if (Map.GridSpacing <= 0)
            {
                Map.GridSpacing = Grid.DefaultSize;
            }

            try
            {
                GameData =  GameDataProvider.GetGameDataFromFiles(game.Fgds.Select(f => f.Path));
            }
            catch(ProviderException)
            {
                // TODO: Error logging
                GameData = new GameData();
            }

            foreach (var wad in game.Wads.OrderBy(x => Path.GetFileName(x.Path)))
            {
                TexturePackage.Load(wad.Path);
            }
            var texList = Map.GetAllTextures();
            TexturePackage.LoadTextureData(texList);

            Map.PostLoadProcess(GameData, TextureHelper.Get);

            Renderer = new RenderManager(this);
            HelperManager = new HelperManager(this);

            if (MapFile != null) Mediator.Publish(EditorMediator.FileOpened, MapFile);
            Mediator.Publish(EditorMediator.DocumentOpened, this);

            // Autosaving
            if (Game.Autosave)
            {
                var at = Math.Max(1, Game.AutosaveTime);
                Scheduler.Schedule(this, Autosave, TimeSpan.FromMinutes(at));
            }
        }

        public void SetActive()
        {
            Editor.Instance.SelectTool(ToolManager.Tools[0]); // todo keep this? cache?

            ViewportManager.AddContext3D(new WidgetLinesRenderable());
            Renderer.Register(ViewportManager.Viewports);
            ViewportManager.AddContextAll(new ToolRenderable());
            ViewportManager.AddContextAll(new HelperRenderable(this));

            _subscriptions.Subscribe();
            HelperManager.UpdateCache();

            Mediator.Publish(EditorMediator.DocumentActivated, this);
        }

        public void SetInactive()
        {
            // todo save state (camera locations, selected tool)
            ViewportManager.ClearContexts();
            HelperManager.ClearCache();

            _subscriptions.Unsubscribe();
        }

        public void Close()
        {
            Scheduler.Clear(this);
        }

        private string GetAutosaveFormatString()
        {
            if (MapFile == null || Path.GetFileNameWithoutExtension(MapFile) == null) return null;
            var we = Path.GetFileNameWithoutExtension(MapFile);
            var ex = Path.GetExtension(MapFile);
            return we + ".auto.{0}" + ex;
        }

        private string GetAutosaveFolder()
        {
            if (Game.UseCustomAutosaveDir && System.IO.Directory.Exists(Game.AutosaveDir)) return Game.AutosaveDir;
            if (MapFile == null || Path.GetDirectoryName(MapFile) == null) return null;
            return Path.GetDirectoryName(MapFile);
        }

        public void Autosave()
        {
            if (!Game.Autosave) return;
            var dir = GetAutosaveFolder();
            var fmt = GetAutosaveFormatString();

            // Only save on change if the game is configured to do so
            if (dir != null && fmt != null && (History.TotalActionsSinceLastAutoSave != 0 || !Game.AutosaveOnlyOnChanged))
            {
                var date = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd-hh-mm-ss");
                var filename = String.Format(fmt, date);
                if (System.IO.File.Exists(filename)) System.IO.File.Delete(filename);

                // Save the file
                MapProvider.SaveMapToFile(Path.Combine(dir, filename), Map);

                // Delete extra autosaves if there is a limit
                if (Game.AutosaveLimit > 0)
                {
                    var asFiles = GetAutosaveFiles(dir);
                    foreach (var file in asFiles.OrderByDescending(x => x.Value).Skip(Game.AutosaveLimit))
                    {
                        if (System.IO.File.Exists(file.Key)) System.IO.File.Delete(file.Key);
                    }
                }

                // Publish event
                Mediator.Publish(EditorMediator.FileAutosaved, this);
                History.TotalActionsSinceLastAutoSave = 0;
            }

            // Reschedule autosave
            var at = Math.Max(1, Game.AutosaveTime);
            Scheduler.Schedule(this, Autosave, TimeSpan.FromMinutes(at));
        }

        public Dictionary<string, DateTime> GetAutosaveFiles(string dir)
        {
            var ret = new Dictionary<string, DateTime>();
            var fs = GetAutosaveFormatString();
            if (fs == null || dir == null) return ret;
            // Search for matching files
            var files = System.IO.Directory.GetFiles(dir, String.Format(fs, "*"));
            foreach (var file in files)
            {
                // Match the date portion with a regex
                var re = Regex.Escape(fs.Replace("{0}", ":")).Replace(":", "{0}");
                var regex = String.Format(re, "(\\d{4})-(\\d{2})-(\\d{2})-(\\d{2})-(\\d{2})-(\\d{2})");
                var match = Regex.Match(Path.GetFileName(file), regex, RegexOptions.IgnoreCase);
                if (!match.Success) continue;

                // Parse the date and add it if it is valid
                DateTime date;
                var result = DateTime.TryParse(String.Format("{0}-{1}-{2}T{3}:{4}:{5}Z",
                                                             match.Groups[1].Value, match.Groups[2].Value,
                                                             match.Groups[3].Value, match.Groups[4].Value,
                                                             match.Groups[5].Value, match.Groups[6].Value),
                                                             CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                                                             out date);
                if (result)
                {
                    ret.Add(file, date);
                }
            }
            return ret;
        }

        public Coordinate Snap(Coordinate c, decimal spacing = 0)
        {
            if (!Map.SnapToGrid) return c;

            var snap = (Select.SnapStyle == SnapStyle.SnapOnAlt && KeyboardState.Alt) ||
                       (Select.SnapStyle == SnapStyle.SnapOffAlt && !KeyboardState.Alt);

            return snap ? c.Snap(spacing == 0 ? Map.GridSpacing : spacing) : c;
        }

        /// <summary>
        /// Performs the action, adds it to the history stack, and optionally updates the display lists
        /// </summary>
        /// <param name="name">The name of the action, for history purposes</param>
        /// <param name="action">The action to perform</param>
        public void PerformAction(string name, IAction action)
        {
            try
            {
                action.Perform(this);
            }
            catch (Exception ex)
            {
                var st = new StackTrace();
                var frames = st.GetFrames() ?? new StackFrame[0];
                var msg = "Action exception: " + name + " (" + action + ")";
                foreach (var frame in frames)
                {
                    var method = frame.GetMethod();
                    msg += "\r\n    " + method.ReflectedType.FullName + "." + method.Name;
                }
                Logging.Logger.ShowException(new Exception(msg, ex), "Error performing action");
            }

            var history = new HistoryAction(name, action);
            History.AddHistoryItem(history);
        }

        public void StartSelectionTransform()
        {
            // todo selection transform shader
            //foreach (var dl in DisplayLists)
            //{
            //    dl.SetTintSelectListEnabled(false);
            //}
            //UpdateDisplayLists(true);
        }

        public void SetSelectListTransform(Matrix4 matrix)
        {
            Renderer.Shader.Bind();
            Renderer.Shader.Set("selectionTransform", matrix);
            Renderer.Shader.Unbind();
        }

        public void EndSelectionTransform()
        {
            Renderer.Shader.Bind();
            Renderer.Shader.Set("selectionTransform", Matrix4.Identity);
            Renderer.Shader.Unbind();
        }

        public void UpdateDisplayLists()
        {
            Map.PartialPostLoadProcess(GameData, TextureHelper.Get);
            HelperManager.UpdateCache();
            Renderer.Update();
            ViewportManager.Viewports.ForEach(vp => vp.UpdateNextFrame());
        }

        public void UpdateDisplayLists(IEnumerable<MapObject> objects)
        {
            Map.PartialPostLoadProcess(GameData, TextureHelper.Get);
            HelperManager.UpdateCache();
            Renderer.UpdatePartial(objects);
            ViewportManager.Viewports.ForEach(vp => vp.UpdateNextFrame());
        }

        public void UpdateDisplayLists(IEnumerable<Face> faces)
        {
            Map.PartialPostLoadProcess(GameData, TextureHelper.Get);
            HelperManager.UpdateCache();
            Renderer.UpdatePartial(faces);
            ViewportManager.Viewports.ForEach(vp => vp.UpdateNextFrame());
        }
    }
}
