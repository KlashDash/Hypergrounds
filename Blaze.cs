using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hypergrounds
{
    public class Blaze
    {
        public bool IsAimHolding { get; private set; }
        public float AimRotation { get; private set; }
        private static readonly float MaxAimTilt = MathHelper.ToRadians(45f);

        public Vector2 AimDirection =>
            new Vector2((FacingRight ? 1f : -1f) * MathF.Cos(AimRotation), MathF.Sin(AimRotation));

        public const int FrameWidth = 16;
        public const int FrameHeight = 16;
        private const float Scale = 1f;

        private const float FrameTime = 0.08f;
        private const float WalkFrameTime = 0.10f;
        private const int RunFrameCount = 8;
        private const int WalkFrameCount = 8;

        private const float IdleFrameTime = .5f;

        // Tunable speeds (pixels per second)
        // --- Speeds / jump (tune here) ---
        // Tunable speeds (pixels per second)
        public static float MoveSpeed = 35f;
        public static float SprintSpeed = 60f;
        public static float Gravity = 400f;
        public static float DashSpeed = 185f;
        public static float DashDuration = 0.25f;
        public static float DashCooldownTime = 2f;

        // Jump = this × player height (change this to tune jump)
        public static float JumpHeightMultiplier = 2f;

        // Accel / stop timing (in frames at 60 FPS)
        private const float AccelFrames = 6f;
        private const float StopFrames = 3f;
        private const float TargetFps = 60f;

        private bool _jumpKeyWasUp = true;

        private bool _isSprinting;
        private bool _leftWasUp = true;
        private bool _rightWasUp = true;
        private float _leftDoubleTapTimer;
        private float _rightDoubleTapTimer;
        private const float DoubleTapWindow = 0.28f;

        private bool _isDashing;
        private float _dashTimer;
        private float _dashCooldown;
        private int _dashDirection = 1;

        private DashParticles _dashFx;
        private float _dashFxTimer;
        private const float DashFxInterval = 0.03f;

        private float _moveAnimGrace;
        private const float MoveAnimGraceTime = 0.08f;

        public Vector2 Position { get; private set; }
        public Vector2 Velocity { get; private set; }
        public bool FacingRight { get; private set; } = true;
        public bool IsOnGround { get; private set; }
        public bool IsMoving { get; private set; }



        public float Width => FrameWidth * Scale;
        public float Height => FrameHeight * Scale;
        public Vector2 Center => Position + new Vector2(Width / 2f, Height / 2f);
        public Rectangle Bounds => new Rectangle(
            (int)MathF.Round(Position.X),
            (int)MathF.Round(Position.Y),
            (int)Width, (int)Height);

        private enum AnimState { Idle, Walk, Run, Jump, Dash }
        private AnimState _animState = AnimState.Idle;
        private AnimState _prevAnimState = AnimState.Idle;
        private int _currentFrame;
        private float _animationTimer;

        private Texture2D _texIdle;
        private Texture2D _texWalk;
        private Texture2D _texRun;
        private Texture2D _texJump;
        private Texture2D _texDash;

        public void LoadContent(Microsoft.Xna.Framework.Content.ContentManager content)
        {
            _texIdle = content.Load<Texture2D>("Graphics/Characters/Blaze/blaze_idle");
            _texWalk = content.Load<Texture2D>("Graphics/Characters/Blaze/blaze_walk");
            _texRun = content.Load<Texture2D>("Graphics/Characters/Blaze/blaze_run");
            _texJump = content.Load<Texture2D>("Graphics/Characters/Blaze/blaze_jump");
            _texDash = content.Load<Texture2D>("Graphics/Characters/Blaze/blaze_dash");
        }

        public void SetDashParticles(DashParticles particles)
        {
            _dashFx = particles;
        }

        private Vector2 Feet() =>
            new Vector2(Position.X + Width / 2f, Position.Y + Height);

        public void Spawn(Vector2 position)
        {
            Position = position;
            Velocity = Vector2.Zero;
            _isDashing = false;
            _dashCooldown = 0f;
            _isSprinting = false;
        }

        public void BeginAimHold(Vector2 mouseWorld)
        {
            IsAimHolding = true;
            Velocity = Vector2.Zero;
            UpdateAimHold(mouseWorld);
        }

        public void UpdateAimHold(Vector2 mouseWorld)
        {
            if (!IsAimHolding) return;
            Vector2 to = mouseWorld - Center;
            if (to.LengthSquared() < 0.001f) return;

            FacingRight = to.X >= 0f;
            float verticalAngle = MathF.Atan2(to.Y, MathF.Abs(to.X));
            AimRotation = MathHelper.Clamp(verticalAngle, -MaxAimTilt, MaxAimTilt);
        }

        public void EndAimHold()
        {
            IsAimHolding = false;
            AimRotation = 0f;
        }

        public void Update(float dt, KeyboardState keyboard, TiledMap map)
        {
            if (IsAimHolding)
            {
                Velocity = Vector2.Zero;
                Vector2 aimPos = Position;
                Vector2 aimVel = Vector2.Zero;
                IsOnGround = map.ResolveCollisions(ref aimPos, ref aimVel, Width, Height);
                Position = aimPos;
                UpdateAnimation(dt);
                return;
            }

            if (_dashCooldown > 0f)
                _dashCooldown -= dt;

            if (!_isDashing && _dashCooldown <= 0f && keyboard.IsKeyDown(Keys.Q))
            {
                _isDashing = true;
                _dashTimer = DashDuration;
                _dashCooldown = DashCooldownTime;
                _dashDirection = FacingRight ? 1 : -1;
                Velocity = new Vector2(_dashDirection * DashSpeed, 0f);
                _currentFrame = 0;
                _animationTimer = 0f;
                _animState = AnimState.Dash;
                _prevAnimState = AnimState.Dash;

                _dashFx?.EmitBurst(Feet(), _dashDirection);
                _dashFxTimer = 0f;
            }

            if (_isDashing)
            {
                _dashTimer -= dt;
                Velocity = new Vector2(_dashDirection * DashSpeed, 0f);
                _animState = AnimState.Dash;

                _dashFxTimer -= dt;
                if (_dashFxTimer <= 0f)
                {
                    _dashFxTimer = DashFxInterval;
                    _dashFx?.EmitTrail(Feet(), _dashDirection);
                }

                if (_dashTimer <= 0f)
                {
                    _isDashing = false;
                    Velocity = new Vector2(0f, Velocity.Y);
                }
            }
            else
            {
                IsMoving = false;

                bool leftHeld = keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.A);
                bool rightHeld = keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D);

                if (_leftDoubleTapTimer > 0f) _leftDoubleTapTimer -= dt;
                if (_rightDoubleTapTimer > 0f) _rightDoubleTapTimer -= dt;

                if (leftHeld && _leftWasUp)
                {
                    if (_leftDoubleTapTimer > 0f) _isSprinting = true;
                    else _leftDoubleTapTimer = DoubleTapWindow;
                }
                if (rightHeld && _rightWasUp)
                {
                    if (_rightDoubleTapTimer > 0f) _isSprinting = true;
                    else _rightDoubleTapTimer = DoubleTapWindow;
                }

                if (!leftHeld && !rightHeld)
                    _isSprinting = false;

                _leftWasUp = !leftHeld;
                _rightWasUp = !rightHeld;

                float targetSpeed = _isSprinting ? SprintSpeed : MoveSpeed;
                float desiredVx = 0f;

                if (leftHeld)
                {
                    desiredVx = -targetSpeed;
                    FacingRight = false;
                    IsMoving = true;
                }
                if (rightHeld)
                {
                    desiredVx = targetSpeed;
                    FacingRight = true;
                    IsMoving = true;
                }

                // Full speed in ~6 frames; stop in ~3 frames
                float accel = targetSpeed * (TargetFps / AccelFrames);
                float decel = targetSpeed * (TargetFps / StopFrames);

                float vx = Velocity.X;
                if (desiredVx != 0f)
                {
                    float rate = accel * dt;
                    if (vx < desiredVx)
                        vx = MathHelper.Min(vx + rate, desiredVx);
                    else if (vx > desiredVx)
                        vx = MathHelper.Max(vx - rate, desiredVx);
                }
                else
                {
                    float rate = decel * dt;
                    if (vx > 0f)
                        vx = MathHelper.Max(vx - rate, 0f);
                    else if (vx < 0f)
                        vx = MathHelper.Min(vx + rate, 0f);
                }

                bool jumpHeld = keyboard.IsKeyDown(Keys.Space) ||
                                keyboard.IsKeyDown(Keys.W) ||
                                keyboard.IsKeyDown(Keys.Up);

                // Jump height = JumpHeightMultiplier × player height
                float jumpHeight = Height * JumpHeightMultiplier;
                float jumpForce = -MathF.Sqrt(2f * Gravity * jumpHeight);

                float vy = Velocity.Y;
                if (jumpHeld && _jumpKeyWasUp && IsOnGround)
                {
                    vy = jumpForce;
                    IsOnGround = false;
                    _currentFrame = 0;
                    _animationTimer = 0f;
                }
                _jumpKeyWasUp = !jumpHeld;

                if (!IsOnGround)
                    vy += Gravity * dt;
                else if (vy > 0f)
                    vy = 0f;

                Velocity = new Vector2(vx, vy);

                if (IsMoving)
                    _moveAnimGrace = MoveAnimGraceTime;
                else if (_moveAnimGrace > 0f)
                    _moveAnimGrace -= dt;
            }

            Vector2 pos = Position;
            Vector2 vel = Velocity;

            map.MoveAndCollideX(ref pos, ref vel, Width, Height, dt);
            bool grounded = map.MoveAndCollideY(ref pos, ref vel, Width, Height, dt);

            pos.X = MathHelper.Clamp(pos.X, 0, map.WorldPixelWidth - Width);

            Position = pos;
            Velocity = vel;
            IsOnGround = grounded;

            if (IsOnGround && Velocity.Y > 0f)
                Velocity = new Vector2(Velocity.X, 0f);

            UpdateAnimation(dt);
        }

        private void UpdateAnimation(float dt)
        {
            if (_isDashing)
                _animState = AnimState.Dash;
            else if (!IsOnGround)
                _animState = AnimState.Jump;
            else if (IsMoving || _moveAnimGrace > 0f)
                _animState = _isSprinting ? AnimState.Run : AnimState.Walk;
            else
                _animState = AnimState.Idle;

            if (_animState != _prevAnimState)
            {
                _currentFrame = 0;
                _animationTimer = 0f;
                _prevAnimState = _animState;
            }

            // Frame counts from strip width (16px per frame)
            int idleFrames = Math.Max(1, (_texIdle?.Width ?? FrameWidth) / FrameWidth); // 32/16 = 2
            int dashFrames = Math.Max(1, (_texDash?.Width ?? FrameWidth) / FrameWidth);

            switch (_animState)
            {
                case AnimState.Idle:
                    // Slower than walk so the 2-frame idle is easy to see
                    _animationTimer += dt;
                    if (_animationTimer >= IdleFrameTime)
                    {
                        _animationTimer -= IdleFrameTime;
                        _currentFrame = (_currentFrame + 1) % idleFrames;
                    }
                    break;

                case AnimState.Walk:
                    _animationTimer += dt;
                    if (_animationTimer >= WalkFrameTime)
                    {
                        _animationTimer -= WalkFrameTime;
                        _currentFrame = (_currentFrame + 1) % WalkFrameCount;
                    }
                    break;

                case AnimState.Run:
                    _animationTimer += dt;
                    if (_animationTimer >= FrameTime)
                    {
                        _animationTimer -= FrameTime;
                        _currentFrame = (_currentFrame + 1) % RunFrameCount;
                    }
                    break;

                case AnimState.Jump:
                    if (Velocity.Y < -200f) _currentFrame = 0;
                    else if (Velocity.Y < -50f) _currentFrame = 1;
                    else if (Velocity.Y < 50f) _currentFrame = 2;
                    else if (Velocity.Y < 250f) _currentFrame = 3;
                    else _currentFrame = 4;
                    break;

                case AnimState.Dash:
                    _animationTimer += dt;
                    if (_animationTimer >= 0.08f)
                    {
                        _animationTimer -= 0.08f;
                        _currentFrame = (_currentFrame + 1) % dashFrames;
                    }
                    break;
            }
        }

        private Texture2D GetCurrentTexture()
        {
            return _animState switch
            {
                AnimState.Idle => _texIdle,
                AnimState.Walk => _texWalk ?? _texRun,
                AnimState.Run => _texRun,
                AnimState.Jump => _texJump,
                AnimState.Dash => _texDash,
                _ => _texIdle
            };
        }

        // pixelsPerUnit = output pixels per world unit (camera zoom * window scale).
        // Sprite is snapped to whole output pixels so it moves in step with the camera.
        public void Draw(SpriteBatch spriteBatch, float pixelsPerUnit = 1f)
        {
            float Snap(float v) => MathF.Round(v * pixelsPerUnit) / pixelsPerUnit;

            Texture2D tex = GetCurrentTexture();
            if (tex == null) return;

            int maxFrame = Math.Max(0, (tex.Width / FrameWidth) - 1);
            int frame = MathHelper.Clamp(_currentFrame, 0, maxFrame);
            Rectangle sourceRect = new Rectangle(frame * FrameWidth, 0, FrameWidth, FrameHeight);

            if (IsAimHolding)
            {
                Vector2 origin = new Vector2(FrameWidth / 2f, FrameHeight / 2f);
                SpriteEffects aimEffects = FacingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                float visualRotation = FacingRight ? AimRotation : -AimRotation;
                Vector2 drawPos = new Vector2(Snap(Center.X), Snap(Center.Y));

                spriteBatch.Draw(tex, drawPos, sourceRect, Color.White,
                    visualRotation, origin, Scale, aimEffects, 0f);
            }
            else
            {
                SpriteEffects effects = FacingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                Vector2 origin = new Vector2(FrameWidth / 2f, FrameHeight);

                // HERE
                Vector2 feetPos = new Vector2(
                    Snap(Position.X + Width / 2f),
                    Snap(Position.Y + Height));

                spriteBatch.Draw(tex, feetPos, sourceRect, Color.White,
                    0f, origin, Scale, effects, 0f);
            }
        }
    }
}