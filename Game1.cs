using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hypergrounds
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        private const int VirtualWidth = 320;
        private const int VirtualHeight = 180;
        private const int Scale = 4;

        private RenderTarget2D _renderTarget;

        private Texture2D _pixel;
        private Texture2D _tileset;

        private TiledMap _map;
        private Blaze _blaze;
        private Camera _camera;
        private DashParticles _dashFx;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            _graphics.PreferredBackBufferWidth = VirtualWidth * Scale;
            _graphics.PreferredBackBufferHeight = VirtualHeight * Scale;
            _graphics.SynchronizeWithVerticalRetrace = true;
            IsFixedTimeStep = true;
            TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        }

        protected override void Initialize()
        {
            _renderTarget = new RenderTarget2D(
                GraphicsDevice, VirtualWidth * Scale, VirtualHeight * Scale,
                false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            _blaze = new Blaze();
            _camera = new Camera(VirtualWidth, VirtualHeight, Scale);
            _dashFx = new DashParticles();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            _blaze.LoadContent(Content);
            _dashFx.Load(GraphicsDevice);
            _blaze.SetDashParticles(_dashFx);

            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });

            try
            {
                _tileset = Content.Load<Texture2D>("Maps/tileset");
            }
            catch
            {
                string path = Path.Combine(Content.RootDirectory, "Maps", "tileset.png");
                if (File.Exists(path))
                {
                    using var stream = TitleContainer.OpenStream(path);
                    _tileset = Texture2D.FromStream(GraphicsDevice, stream);
                }
            }

            _map = new TiledMap();
            string mapPath = FindMapPath("level1.json");
            _map.Load(mapPath, GraphicsDevice, _tileset, _pixel);

            float spawnX = 4 * _map.TileWidth;
            float spawnY = _map.GroundTopY - _blaze.Height;
            if (spawnY < 0 || _map.GroundTopY >= _map.WorldPixelHeight)
                spawnY = (_map.Height - 3) * _map.TileHeight;
            _blaze.Spawn(new Vector2(spawnX, spawnY));
        }

        private static string FindMapPath(string fileName)
        {
            string[] candidates =
            {
                Path.Combine("Content", "Maps", fileName),
                Path.Combine(AppContext.BaseDirectory, "Content", "Maps", fileName),
                Path.Combine(AppContext.BaseDirectory, "Maps", fileName),
            };

            foreach (string path in candidates)
                if (File.Exists(path)) return path;

            throw new FileNotFoundException(
                "Could not find map file '" + fileName + "'. " +
                "Make sure Content/Maps is copied to the output directory.");
        }

        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
                Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            var keyboard = Keyboard.GetState();
            var mouse = Mouse.GetState();

            _blaze.Update(dt, keyboard, _map);
            _dashFx.Update(dt);
            _camera.Follow(_blaze.Center, _map.WorldPixelWidth, _map.WorldPixelHeight, dt);
            _camera.UpdateShake(dt);

            Vector2 mouseWorld = _camera.ScreenToWorld(new Vector2(mouse.X, mouse.Y));

            if (mouse.RightButton == ButtonState.Pressed)
            {
                if (!_blaze.IsAimHolding)
                    _blaze.BeginAimHold(mouseWorld);
                else
                    _blaze.UpdateAimHold(mouseWorld);
            }
            else if (_blaze.IsAimHolding)
            {
                _blaze.EndAimHold();
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            // --- Pass 1: draw world into virtual RT ---
            GraphicsDevice.SetRenderTarget(_renderTarget);
            GraphicsDevice.Clear(Color.CornflowerBlue);

            _spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                null,
                _camera.GetTransform());

            _map.Draw(_spriteBatch);
            _dashFx.Draw(_spriteBatch);
            _blaze.Draw(_spriteBatch, _camera.PixelsPerUnit);

            _spriteBatch.End();   // must End before next Begin

            // --- Pass 2: scale RT to window ---
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone);

            _spriteBatch.Draw(
                _renderTarget,
                new Rectangle(0, 0, VirtualWidth * Scale, VirtualHeight * Scale),
                Color.White);

            _spriteBatch.End();

            base.Draw(gameTime);
        }
    }
}