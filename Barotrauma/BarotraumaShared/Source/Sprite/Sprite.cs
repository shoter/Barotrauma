﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public partial class Sprite
    {
        public static IEnumerable<Sprite> LoadedSprites
        {
            get { return list; }
        }

        private static HashSet<Sprite> list = new HashSet<Sprite>();

        //the file from which the texture is loaded
        //if two sprites use the same file, they share the same texture
        private string file;

        /// <summary>
        /// Reference to the xml element from where the sprite was created. Can be null if the sprite was not defined in xml!
        /// </summary>
        public XElement SourceElement { get; private set; }

        //the area in the texture that is supposed to be drawn
        private Rectangle sourceRect;

        //the offset used when drawing the sprite
        protected Vector2 offset;

        protected Vector2 origin;

        //the size of the drawn sprite, if larger than the source,
        //the sprite is tiled to fill the target size
        public Vector2 size;

        public float rotation;

        public SpriteEffects effects = SpriteEffects.None;

        protected float depth;

        public Rectangle SourceRect
        {
            get { return sourceRect; }
            set { sourceRect = value; }
        }

        public float Depth
        {
            get { return depth; }
            set { depth = MathHelper.Clamp(value, 0.001f, 0.999f); }
        }

        /// <summary>
        /// In pixels
        /// </summary>
        public Vector2 Origin
        {
            get { return origin; }
            set
            {
                origin = value;
                _relativeOrigin = Vector2.Clamp(new Vector2(origin.X / sourceRect.Width, origin.Y / sourceRect.Height), Vector2.Zero, Vector2.One);
            }
        }

        private Vector2 _relativeOrigin;
        /// <summary>
        /// 0 - 1
        /// </summary>
        public Vector2 RelativeOrigin
        {
            get => _relativeOrigin;
            set
            {
                _relativeOrigin = Vector2.Clamp(value, Vector2.Zero, Vector2.One);
                origin = new Vector2(_relativeOrigin.X * sourceRect.Width, _relativeOrigin.Y * sourceRect.Height);
            }
        }
        
        public string FilePath
        {
            get { return file; }
        }

        public override string ToString()
        {
            return FilePath + ": " + sourceRect;
        }

        public string ID { get; private set; }

        partial void LoadTexture(ref Vector4 sourceVector, ref bool shouldReturn, bool premultiplyAlpha = true);
        partial void CalculateSourceRect();

        public Sprite(XElement element, string path = "", string file = "")
        {
            SourceElement = element;
            if (file == "")
            {
                file = SourceElement.GetAttributeString("texture", "");
            }
            if (file == "")
            {
                DebugConsole.ThrowError("Sprite " + SourceElement + " doesn't have a texture specified!");
                return;
            }
            if (!string.IsNullOrEmpty(path))
            {
                if (!path.EndsWith("/")) path += "/";
            }
            this.file = path + file;
            Vector4 sourceVector = SourceElement.GetAttributeVector4("sourcerect", Vector4.Zero);
            bool shouldReturn = false;
            LoadTexture(ref sourceVector, ref shouldReturn, SourceElement.GetAttributeBool("premultiplyalpha", false));
            if (shouldReturn) return;
            sourceRect = new Rectangle((int)sourceVector.X, (int)sourceVector.Y, (int)sourceVector.Z, (int)sourceVector.W);
            size = SourceElement.GetAttributeVector2("size", Vector2.One);
            size.X *= sourceRect.Width;
            size.Y *= sourceRect.Height;
            RelativeOrigin = SourceElement.GetAttributeVector2("origin", new Vector2(0.5f, 0.5f));
            Depth = SourceElement.GetAttributeFloat("depth", 0.001f);
            ID = GetID(SourceElement);
            list.Add(this);
        }

        internal void LoadParams(SpriteParams spriteParams, bool isFlipped)
        {
            SourceElement = spriteParams.Element;
            sourceRect = spriteParams.SourceRect;
            RelativeOrigin = spriteParams.Origin;
            if (isFlipped)
            {
                Origin = new Vector2(sourceRect.Width - origin.X, origin.Y);
            }
            depth = spriteParams.Depth;
            // TODO: size?
        }

        public Sprite(string newFile, Vector2 newOrigin, bool preMultiplyAlpha = true)
        {
            Init(newFile, newOrigin: newOrigin, preMultiplyAlpha: preMultiplyAlpha);
            list.Add(this);
        }
        
        public Sprite(string newFile, Rectangle? sourceRectangle, Vector2? origin = null, float rotation = 0, bool preMultiplyAlpha = true)
        {
            Init(newFile, sourceRectangle: sourceRectangle, newOrigin: origin, newRotation: rotation, preMultiplyAlpha: preMultiplyAlpha);
            list.Add(this);
        }
        
        private void Init(string newFile, Rectangle? sourceRectangle = null, Vector2? newOrigin = null, Vector2? newOffset = null, float newRotation = 0, 
            bool preMultiplyAlpha = true)
        {
            file = newFile;
            Vector4 sourceVector = Vector4.Zero;
            bool shouldReturn = false;
            LoadTexture(ref sourceVector, ref shouldReturn, preMultiplyAlpha);
            if (shouldReturn) return;
            if (sourceRectangle.HasValue)
            {
                sourceRect = sourceRectangle.Value;
            }
            else
            {
                CalculateSourceRect();
            }
            offset = newOffset ?? Vector2.Zero;
            if (newOrigin.HasValue)
            {
                RelativeOrigin = newOrigin.Value;
            }
            size = new Vector2(sourceRect.Width, sourceRect.Height);
            rotation = newRotation;
        }

        /// <summary>
        /// Creates a supposedly unique id from the parent element. If the parent element is not found, uses the sprite element.
        /// TODO: If there are multiple elements with exactly the same data, the ids will fail. -> Is there a better way to identify the sprites?
        /// </summary>
        public static string GetID(XElement sourceElement)
        {
            if (sourceElement == null) { return string.Empty; }
            var parentElement = sourceElement.Parent;
            return parentElement != null ? sourceElement.ToString() + parentElement.ToString() : sourceElement.ToString();
        }

        public void Remove()
        {
            list.Remove(this);

            DisposeTexture();
        }

        partial void DisposeTexture();

        /// <summary>
        /// Works only if there is a name attribute defined for the sprite.
        /// </summary>
        public void ReloadXML()
        {
            if (SourceElement == null) { return; }
            var doc = XMLExtensions.TryLoadXml(SourceElement.ParseContentPathFromUri());
            if (doc == null || doc.Root == null) { return; }
            string name = SourceElement.GetAttributeString("name", string.Empty);
            var sourceElements = doc.Descendants("sprite").Where(e => e.GetAttributeString("name", "Not found") == name);
            if (sourceElements.Multiple())
            {
                DebugConsole.NewMessage($"[Sprite] Multiple matching elements found by name {name}! Cannot reload the xml for element \"{SourceElement.ToString()}\"!", Color.Yellow);
            }
            else if (sourceElements.None())
            {
                //DebugConsole.NewMessage($"[Sprite] Cannot find matching source element by comparing the name attribute ({name})! Cannot reload the xml for element \"{SourceElement.ToString()}\"!", Color.Yellow);
            }
            else
            {
                SourceElement = sourceElements.Single();
                Vector4 sourceVector = SourceElement.GetAttributeVector4("sourcerect", Vector4.Zero);
                sourceRect = new Rectangle((int)sourceVector.X, (int)sourceVector.Y, (int)sourceVector.Z, (int)sourceVector.W);
                size = SourceElement.GetAttributeVector2("size", Vector2.One);
                size.X *= sourceRect.Width;
                size.Y *= sourceRect.Height;
                RelativeOrigin = SourceElement.GetAttributeVector2("origin", new Vector2(0.5f, 0.5f));
                Depth = SourceElement.GetAttributeFloat("depth", 0.001f);
                ID = GetID(SourceElement);
            }
        }
    }
}

