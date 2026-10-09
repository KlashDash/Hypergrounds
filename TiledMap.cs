using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Hypergrounds
{
    /// <summary>
    /// Loads a Tiled JSON map from Content/Maps.
    /// Solid tiles = Collision/Solid layer, OR local tile id 0 (gid 1 floor).
    /// </summary>
    public class TiledMap
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int TileWidth { get; private set; }
        public int TileHeight { get; private set; }

        public int WorldPixelWidth => Width * TileWidth;
        public int WorldPixelHeight => Height * TileHeight;
        public int GroundTopY { get; private set; }

        private int[,] _visualGids;
        private bool[,] _solid;
        private Texture2D _tilesetTexture;
        private int _tilesetColumns = 1;
        private int _tilesetFirstGid = 1;

        // Local tileset ids (0-based) that are always solid (floor tile in level1)
        private static readonly HashSet<int> _solidTileIds = new HashSet<int> { 0 };

        private Texture2D _pixel;
        private readonly Color _fallbackFloorColor = new Color(50, 50, 50);

        public void Load(string jsonPath, GraphicsDevice graphicsDevice, Texture2D tilesetTexture = null, Texture2D pixel = null)
        {
            _pixel = pixel;
            _tilesetTexture = tilesetTexture;

            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Tiled map not found: " + jsonPath);

            string json = File.ReadAllText(jsonPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            TiledJsonMap map = JsonSerializer.Deserialize<TiledJsonMap>(json, options)
                ?? throw new Exception("Failed to parse Tiled JSON: " + jsonPath);

            Width = Math.Max(1, map.Width);
            Height = Math.Max(1, map.Height);
            TileWidth = map.TileWidth > 0 ? map.TileWidth : 16;
            TileHeight = map.TileHeight > 0 ? map.TileHeight : 16;

            _visualGids = new int[Width, Height];
            _solid = new bool[Width, Height];
            GroundTopY = Height * TileHeight;

            if (map.Tilesets != null && map.Tilesets.Count > 0)
            {
                var ts = map.Tilesets[0];
                _tilesetFirstGid = ts.FirstGid > 0 ? ts.FirstGid : 1;
                if (ts.Columns > 0)
                    _tilesetColumns = ts.Columns;
                else if (_tilesetTexture != null && TileWidth > 0)
                    _tilesetColumns = Math.Max(1, _tilesetTexture.Width / TileWidth);
            }
            else if (_tilesetTexture != null && TileWidth > 0)
            {
                _tilesetColumns = Math.Max(1, _tilesetTexture.Width / TileWidth);
            }

            if (map.Layers == null)
                return;

            foreach (var layer in map.Layers)
            {
                if (layer.Type != "tilelayer")
                    continue;

                if (layer.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                    continue;

                int[] data;
                try { data = ParseLayerData(layer.Data, layer.Encoding, layer.Compression); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TiledMap: failed to parse layer '{layer.Name}': {ex.Message}");
                    continue;
                }

                string name = (layer.Name ?? "").Trim().ToLowerInvariant();
                bool isCollision = name is "collision" or "solid" or "collisions";
                bool isVisual = name is "ground" or "tiles" or "tile" or "foreground" or "background"
                                || (!isCollision && layer.Visible);

                for (int i = 0; i < data.Length && i < Width * Height; i++)
                {
                    int x = i % Width;
                    int y = i / Width;
                    int tileId = data[i] & 0x1FFFFFFF;

                    if (tileId == 0)
                        continue;

                    int localId = tileId - _tilesetFirstGid;
                    bool isForcedSolidTile = _solidTileIds.Contains(localId);

                    // Only collision layers + forced solid tile ids — NOT every painted tile
                    if (isCollision || isForcedSolidTile)
                    {
                        _solid[x, y] = true;
                        int top = y * TileHeight;
                        if (top < GroundTopY)
                            GroundTopY = top;
                    }

                    if (isVisual)
                        _visualGids[x, y] = tileId;
                }
            }
        }

        private static int[] ParseLayerData(JsonElement data, string encoding, string compression)
        {
            if (data.ValueKind == JsonValueKind.Array)
            {
                int[] result = new int[data.GetArrayLength()];
                int i = 0;
                foreach (var el in data.EnumerateArray())
                    result[i++] = el.GetInt32();
                return result;
            }

            if (data.ValueKind == JsonValueKind.String)
            {
                string raw = data.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(raw))
                    return Array.Empty<int>();

                encoding = (encoding ?? "").Trim().ToLowerInvariant();
                compression = (compression ?? "").Trim().ToLowerInvariant();

                if (encoding == "base64")
                {
                    byte[] bytes = Convert.FromBase64String(raw.Trim());

                    switch (compression)
                    {
                        case "zlib":
                            bytes = Decompress(bytes, useGzip: false);
                            break;
                        case "gzip":
                            bytes = Decompress(bytes, useGzip: true);
                            break;
                        case "":
                        case "none":
                            break;
                        default:
                            throw new Exception($"Unsupported Tiled compression '{compression}'.");
                    }

                    if (bytes.Length % 4 != 0)
                        throw new Exception("Decoded Tiled layer data is not a multiple of 4 bytes.");

                    int[] tileIds = new int[bytes.Length / 4];
                    for (int i = 0; i < tileIds.Length; i++)
                        tileIds[i] = BitConverter.ToInt32(bytes, i * 4);
                    return tileIds;
                }

                if (encoding == "csv" || encoding == "")
                {
                    string[] parts = raw.Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int[] result = new int[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        result[i] = int.Parse(parts[i].Trim());
                    return result;
                }

                throw new Exception($"Unsupported Tiled layer encoding '{encoding}'.");
            }

            throw new Exception("Unsupported layer data type in Tiled JSON.");
        }

        private static byte[] Decompress(byte[] input, bool useGzip)
        {
            using var inputStream = new MemoryStream(input);
            using var outputStream = new MemoryStream();
            using (Stream decompressor = useGzip
                ? new GZipStream(inputStream, CompressionMode.Decompress)
                : new ZLibStream(inputStream, CompressionMode.Decompress))
            {
                decompressor.CopyTo(outputStream);
            }
            return outputStream.ToArray();
        }

        public bool IsSolid(int tileX, int tileY)
        {
            if (tileX < 0 || tileX >= Width || tileY < 0 || tileY >= Height)
                return false;
            return _solid[tileX, tileY];
        }

        /// <summary>
        /// Move on X then resolve walls. Does not change Y.
        /// </summary>
        public void MoveAndCollideX(ref Vector2 position, ref Vector2 velocity, float bodyWidth, float bodyHeight, float dt)
        {
            const float Skin = 0.05f;
            position.X += velocity.X * dt;

            float left = position.X;
            float top = position.Y + Skin;
            float right = left + bodyWidth;
            float bottom = position.Y + bodyHeight - Skin;

            int startX = MathHelper.Max(0, (int)Math.Floor(left / TileWidth));
            int endX = MathHelper.Min(Width - 1, (int)Math.Floor((right - 0.001f) / TileWidth));
            int startY = MathHelper.Max(0, (int)Math.Floor(top / TileHeight));
            int endY = MathHelper.Min(Height - 1, (int)Math.Floor((bottom - 0.001f) / TileHeight));

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    if (!_solid[x, y]) continue;

                    float tileLeft = x * TileWidth;
                    float tileRight = tileLeft + TileWidth;
                    if (right <= tileLeft || left >= tileRight)
                        continue;

                    if (velocity.X > 0f)
                    {
                        position.X = tileLeft - bodyWidth;
                        velocity.X = 0f;
                    }
                    else if (velocity.X < 0f)
                    {
                        position.X = tileRight;
                        velocity.X = 0f;
                    }

                    left = position.X;
                    right = position.X + bodyWidth;
                }
            }
        }

        /// <summary>
        /// Move on Y then resolve floors/ceilings. Returns whether standing on ground.
        /// </summary>
        public bool MoveAndCollideY(ref Vector2 position, ref Vector2 velocity, float bodyWidth, float bodyHeight, float dt)
        {
            const float Skin = 0.05f;
            position.Y += velocity.Y * dt;
            bool onGround = false;

            float left = position.X + Skin;
            float top = position.Y;
            float right = position.X + bodyWidth - Skin;
            float bottom = top + bodyHeight;

            int startX = MathHelper.Max(0, (int)Math.Floor(left / TileWidth));
            int endX = MathHelper.Min(Width - 1, (int)Math.Floor((right - 0.001f) / TileWidth));
            int startY = MathHelper.Max(0, (int)Math.Floor(top / TileHeight));
            int endY = MathHelper.Min(Height - 1, (int)Math.Floor((bottom - 0.001f) / TileHeight));

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    if (!_solid[x, y]) continue;

                    float tileTop = y * TileHeight;
                    float tileBottom = tileTop + TileHeight;
                    if (bottom <= tileTop || top >= tileBottom)
                        continue;

                    if (velocity.Y >= 0f)
                    {
                        position.Y = tileTop - bodyHeight;
                        velocity.Y = 0f;
                        onGround = true;
                    }
                    else
                    {
                        position.Y = tileBottom;
                        velocity.Y = 0f;
                    }

                    top = position.Y;
                    bottom = position.Y + bodyHeight;
                }
            }

            // Small ground probe — stops edge flicker
            if (!onGround && velocity.Y >= 0f)
            {
                float feet = position.Y + bodyHeight + 1.0f;
                int footY = (int)Math.Floor(feet / TileHeight);
                if (footY >= 0 && footY < Height)
                {
                    for (int x = startX; x <= endX; x++)
                    {
                        if (!_solid[x, footY]) continue;
                        float tileTop = footY * TileHeight;
                        if (position.Y + bodyHeight <= tileTop + 1.5f)
                        {
                            position.Y = tileTop - bodyHeight;
                            velocity.Y = 0f;
                            onGround = true;
                            break;
                        }
                    }
                }
            }

            return onGround;
        }

        /// <summary>
        /// Push out of overlaps without moving first (used by aim-hold).
        /// </summary>
        public bool ResolveCollisions(ref Vector2 position, ref Vector2 velocity, float bodyWidth, float bodyHeight)
        {
            bool onGround = false;

            float left = position.X;
            float top = position.Y;
            float right = left + bodyWidth;
            float bottom = top + bodyHeight;

            int startX = MathHelper.Max(0, (int)Math.Floor(left / TileWidth) - 1);
            int endX = MathHelper.Min(Width - 1, (int)Math.Floor(right / TileWidth) + 1);
            int startY = MathHelper.Max(0, (int)Math.Floor(top / TileHeight) - 1);
            int endY = MathHelper.Min(Height - 1, (int)Math.Floor(bottom / TileHeight) + 1);

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    if (!_solid[x, y]) continue;

                    float tileLeft = x * TileWidth;
                    float tileTop = y * TileHeight;
                    float tileRight = tileLeft + TileWidth;
                    float tileBottom = tileTop + TileHeight;

                    if (right <= tileLeft || left >= tileRight || bottom <= tileTop || top >= tileBottom)
                        continue;

                    float oxL = right - tileLeft;
                    float oxR = tileRight - left;
                    float oyT = bottom - tileTop;
                    float oyB = tileBottom - top;
                    float minX = MathHelper.Min(oxL, oxR);
                    float minY = MathHelper.Min(oyT, oyB);

                    if (minX < minY)
                    {
                        position.X += (oxL < oxR) ? -oxL : oxR;
                        velocity.X = 0f;
                        left = position.X;
                        right = left + bodyWidth;
                    }
                    else
                    {
                        if (oyT < oyB)
                        {
                            position.Y -= oyT;
                            velocity.Y = 0f;
                            onGround = true;
                        }
                        else
                        {
                            position.Y += oyB;
                            velocity.Y = 0f;
                        }
                        top = position.Y;
                        bottom = top + bodyHeight;
                    }
                }
            }

            return onGround;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    int gid = _visualGids[x, y];
                    if (gid == 0 && !_solid[x, y])
                        continue;

                    var dest = new Rectangle(x * TileWidth, y * TileHeight, TileWidth, TileHeight);

                    if (_tilesetTexture != null && gid > 0)
                    {
                        int localId = Math.Max(0, gid - _tilesetFirstGid);
                        int col = localId % _tilesetColumns;
                        int row = localId / _tilesetColumns;
                        var source = new Rectangle(col * TileWidth, row * TileHeight, TileWidth, TileHeight);
                        spriteBatch.Draw(_tilesetTexture, dest, source, Color.White);
                    }
                    else if (_solid[x, y] && _pixel != null)
                    {
                        spriteBatch.Draw(_pixel, dest, _fallbackFloorColor);
                    }
                }
            }
        }

        private class TiledJsonMap
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public int TileWidth { get; set; }
            public int TileHeight { get; set; }
            public List<TiledJsonLayer> Layers { get; set; }
            public List<TiledJsonTileset> Tilesets { get; set; }
        }

        private class TiledJsonLayer
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public bool Visible { get; set; } = true;
            public JsonElement Data { get; set; }
            public string Encoding { get; set; }
            public string Compression { get; set; }
        }

        private class TiledJsonTileset
        {
            [JsonPropertyName("firstgid")]
            public int FirstGid { get; set; }
            public int Columns { get; set; }
            [JsonPropertyName("tilecount")]
            public int TileCount { get; set; }
            public string Image { get; set; }
        }
    }
}