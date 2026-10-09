using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Hypergrounds
{
    /// <summary>
    /// Simple pixel dust for dash trails. Not a full VFX system.
    /// </summary>
    public class DashParticles
    {
        private struct Particle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Life;
            public float MaxLife;
            public float Size;
            public Color Color;
        }

        private readonly Particle[] _particles;
        private readonly Random _rng = new Random();
        private Texture2D _pixel;

        public DashParticles(int capacity = 48)
        {
            _particles = new Particle[capacity];
        }

        public void Load(GraphicsDevice device)
        {
            _pixel = new Texture2D(device, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }

        public void EmitBurst(Vector2 feet, int direction)
        {
            for (int i = 0; i < 8; i++)
                Spawn(feet, direction, burst: true);
        }

        public void EmitTrail(Vector2 feet, int direction)
        {
            Spawn(feet, direction, burst: false);
            Spawn(feet, direction, burst: false);
        }

        private void Spawn(Vector2 feet, int direction, bool burst)
        {
            int slot = -1;
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].Life <= 0f)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0) return;

            float side = -direction;
            float jx = (float)(_rng.NextDouble() * 4.0 - 2.0);
            float jy = (float)(_rng.NextDouble() * 6.0 - 3.0);

            float speed = burst ? 40f : 20f;
            var vel = new Vector2(
                side * speed * (0.5f + (float)_rng.NextDouble() * 0.5f) + jx * 3f,
                jy * (burst ? 12f : 6f));

            byte shade = (byte)_rng.Next(160, 230);
            _particles[slot] = new Particle
            {
                Position = feet + new Vector2(jx, jy * 0.3f),
                Velocity = vel,
                Life = burst ? 0.28f : 0.18f,
                MaxLife = burst ? 0.28f : 0.18f,
                Size = burst ? _rng.Next(1, 3) : 1,
                Color = new Color(shade, shade, shade, (byte)255)
            };
        }

        public void Update(float dt)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].Life <= 0f) continue;

                _particles[i].Life -= dt;
                _particles[i].Position += _particles[i].Velocity * dt;
                _particles[i].Velocity *= 0.90f;
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (_pixel == null) return;

            for (int i = 0; i < _particles.Length; i++)
            {
                ref var p = ref _particles[i];
                if (p.Life <= 0f) continue;

                float t = MathHelper.Clamp(p.Life / p.MaxLife, 0f, 1f);
                var c = p.Color * t;
                int s = Math.Max(1, (int)MathF.Round(p.Size));
                int x = (int)MathF.Round(p.Position.X);
                int y = (int)MathF.Round(p.Position.Y);
                spriteBatch.Draw(_pixel, new Rectangle(x, y, s, s), c);
            }
        }
    }
}