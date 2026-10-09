using System;
using Microsoft.Xna.Framework;

namespace Hypergrounds
{
    public class Camera
    {
        public Vector2 Position { get; private set; }
        public float Zoom { get; private set; } = 1f;
        public float PixelsPerUnit => Zoom * _pixelScale;

        private readonly int _screenWidth;
        private readonly int _screenHeight;
        private int _worldWidth;
        private int _worldHeight;

        private const float FollowLerpSpeed = 3f;
        private const float DesiredZoom = 1.25f;

        private float _shakeTime;
        private float _shakeDuration;
        private float _shakeStrength;
        private Vector2 _shakeOffset;
        private readonly Random _shakeRandom = new Random();

        // Output pixels per virtual pixel (window scale). The world is drawn at
        // Zoom * PixelScale so every source pixel maps to a whole number of output pixels.
        private readonly int _pixelScale;

        public Camera(int screenWidth, int screenHeight, int pixelScale = 1)
        {
            _screenWidth = screenWidth;
            _screenHeight = screenHeight;
            _pixelScale = pixelScale;
        }

        public void Shake(float strength, float duration)
        {
            _shakeStrength = Math.Max(_shakeStrength, strength);
            _shakeDuration = Math.Max(_shakeDuration, duration);
            _shakeTime = _shakeDuration;
        }

        public void UpdateShake(float dt)
        {
            if (_shakeTime <= 0f)
            {
                _shakeOffset = Vector2.Zero;
                return;
            }

            _shakeTime -= dt;
            if (_shakeTime <= 0f)
            {
                _shakeTime = 0f;
                _shakeStrength = 0f;
                _shakeOffset = Vector2.Zero;
                return;
            }

            float falloff = _shakeDuration > 0f ? _shakeTime / _shakeDuration : 0f;
            float magnitude = _shakeStrength * falloff;
            _shakeOffset = new Vector2(
                ((float)_shakeRandom.NextDouble() * 2f - 1f) * magnitude,
                ((float)_shakeRandom.NextDouble() * 2f - 1f) * magnitude);
        }

        public void Follow(Vector2 targetCenter, int worldWidth, int worldHeight, float dt)
        {
            _worldWidth = worldWidth;
            _worldHeight = worldHeight;
            Zoom = ComputeZoom(worldWidth, worldHeight);

            float viewWidth = _screenWidth / Zoom;
            float viewHeight = _screenHeight / Zoom;

            float targetX = targetCenter.X - viewWidth / 2f;
            float targetY = targetCenter.Y - viewHeight / 2f;

            targetX = MathHelper.Clamp(targetX, 0, Math.Max(0, worldWidth - viewWidth));
            targetY = MathHelper.Clamp(targetY, 0, Math.Max(0, worldHeight - viewHeight));

            float t = 1f - MathF.Exp(-FollowLerpSpeed * dt);
            Position = Vector2.Lerp(Position, new Vector2(targetX, targetY), t);
        }

        private float ComputeZoom(int worldWidth, int worldHeight)
        {
            return DesiredZoom; // fixed — no fractional fit zoom
        }


        public Matrix GetTransform()
        {
            float drawnWidth = _worldWidth * Zoom;
            float drawnHeight = _worldHeight * Zoom;

            float offsetX = drawnWidth < _screenWidth ? (_screenWidth - drawnWidth) * 0.5f : 0f;
            float offsetY = drawnHeight < _screenHeight ? (_screenHeight - drawnHeight) * 0.5f : 0f;

            // World -> output (window) pixels. Zoom and pixel scale are combined so
            // 1.25 * 4 = 5 whole output pixels per source pixel (no warping).
            float total = Zoom * _pixelScale;

            // Snap in *output* pixels, then convert back to world units
            float camX = MathF.Round(Position.X * total) / total;
            float camY = MathF.Round(Position.Y * total) / total;

            return
                Matrix.CreateTranslation(-camX, -camY, 0f) *
                Matrix.CreateScale(total, total, 1f) *
                Matrix.CreateTranslation(
                    MathF.Round(offsetX * _pixelScale),
                    MathF.Round(offsetY * _pixelScale),
                    0f) *
                Matrix.CreateTranslation(
                    MathF.Round(_shakeOffset.X) * _pixelScale,
                    MathF.Round(_shakeOffset.Y) * _pixelScale,
                    0f);
        }

        // screenPos is in output (window) pixels
        public Vector2 ScreenToWorld(Vector2 screenPos)
        {
            Matrix inverse = Matrix.Invert(GetTransform());
            return Vector2.Transform(screenPos, inverse);
        }
    }
}