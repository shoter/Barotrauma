using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using System;

namespace Barotrauma
{
    public abstract class GUIComponent
    {
        #region Hierarchy
        public GUIComponent Parent => RectTransform.Parent?.GUIComponent;
        
        public IEnumerable<GUIComponent> Children => RectTransform.Children.Select(c => c.GUIComponent);
        
        public T GetChild<T>() where T : GUIComponent
        {
            return Children.FirstOrDefault(c => c is T) as T;
        }

        public T GetAnyChild<T>() where T : GUIComponent
        {
            return GetAllChildren().FirstOrDefault(c => c is T) as T;
        }

        public GUIComponent GetChild(int index)
        {
            if (index < 0 || index >= CountChildren) return null;
            return RectTransform.GetChild(index).GUIComponent;
        }

        public int GetChildIndex(GUIComponent child)
        {
            if (child == null) return -1;
            return RectTransform.GetChildIndex(child.RectTransform);
        }

        public GUIComponent GetChildByUserData(object obj)
        {
            foreach (GUIComponent child in Children)
            {
                if (child.UserData == obj || (child.userData != null && child.userData.Equals(obj))) return child;
            }
            return null;
        }

        /// <summary>
        /// Returns all child elements in the hierarchy.
        /// If the component has RectTransform, it's more efficient to use RectTransform.GetChildren and access the GUIComponent property directly.
        /// </summary>
        public IEnumerable<GUIComponent> GetAllChildren()
        {
            return RectTransform.GetAllChildren().Select(c => c.GUIComponent);
        }

        public bool IsParentOf(GUIComponent component, bool recursive = true)
        {
            if (component == null) { return false; }
            return RectTransform.IsParentOf(component.RectTransform, recursive);
        }

        public virtual void RemoveChild(GUIComponent child)
        {
            if (child == null) return;
            child.RectTransform.Parent = null;
        }

        // TODO: refactor?
        public GUIComponent FindChild(object userData, bool recursive = false)
        {
            var matchingChild = Children.FirstOrDefault(c => c.userData == userData);
            if (recursive && matchingChild == null)
            {
                foreach (GUIComponent child in Children)
                {
                    matchingChild = child.FindChild(userData, recursive);
                    if (matchingChild != null) return matchingChild;
                }
            }

            return matchingChild;
        }

        public IEnumerable<GUIComponent> FindChildren(object userData)
        {
            return Children.Where(c => c.userData == userData);
        }

        public virtual void ClearChildren()
        {
            RectTransform.ClearChildren();
        }

        public void SetAsLastChild()
        {
            RectTransform.SetAsLastChild();
        }
        #endregion

        public bool AutoUpdate { get; set; } = true;
        public bool AutoDraw { get; set; } = true;
        public int UpdateOrder { get; set; }

        public Action<GUIComponent> OnAddedToGUIUpdateList;
        
        public enum ComponentState { None, Hover, Pressed, Selected };

        protected Alignment alignment;

        protected GUIComponentStyle style;

        protected object userData;
        
        public bool CanBeFocused;
        
        protected Color color;
        protected Color hoverColor;
        protected Color selectedColor;
        protected Color pressedColor;

        protected ComponentState state;

        protected Color flashColor;
        protected float flashDuration = 1.5f;
        protected float flashTimer;

        public bool IgnoreLayoutGroups;

        public virtual ScalableFont Font
        {
            get;
            set;
        }

        public virtual string ToolTip
        {
            get;
            set;
        }

        public GUIComponentStyle Style
        {
            get { return style; }
        }

        public bool Visible
        {
            get;
            set;
        }

        protected bool enabled;
        public virtual bool Enabled
        {
            get { return enabled; }
            set { enabled = value; }
        }

        public bool TileSprites;

        private static GUITextBlock toolTipBlock;

        public Vector2 Center
        {
            get { return new Vector2(Rect.Center.X, Rect.Center.Y); }
        }

        protected Rectangle ClampRect(Rectangle r)
        {
            if (Parent == null || !ClampMouseRectToParent) return r;
            Rectangle parentRect = Parent.ClampRect(Parent.Rect);
            if (parentRect.Width <= 0 || parentRect.Height <= 0) return Rectangle.Empty;
            if (parentRect.X > r.X)
            {
                int diff = parentRect.X - r.X;
                r.X = parentRect.X;
                r.Width -= diff;
            }
            if (parentRect.Y > r.Y)
            {
                int diff = parentRect.Y - r.Y;
                r.Y = parentRect.Y;
                r.Height -= diff;
            }
            if (parentRect.X + parentRect.Width < r.X + r.Width)
            {
                int diff = (r.X + r.Width) - (parentRect.X + parentRect.Width);
                r.Width -= diff;
            }
            if (parentRect.Y + parentRect.Height < r.Y + r.Height)
            {
                int diff = (r.Y + r.Height) - (parentRect.Y + parentRect.Height);
                r.Height -= diff;
            }
            if (r.Width <= 0 || r.Height <= 0) return Rectangle.Empty;
            return r;
        }

        public virtual Rectangle Rect
        {
            get { return RectTransform.Rect; }
        }

        public bool ClampMouseRectToParent { get; set; } = false;
        public virtual Rectangle MouseRect
        {
            get
            {
                if (!CanBeFocused) return Rectangle.Empty;
                return ClampMouseRectToParent ? ClampRect(Rect) : Rect;
            }
        }

        public Dictionary<ComponentState, List<UISprite>> sprites;

        public SpriteEffects SpriteEffects;

        public virtual Color OutlineColor { get; set; }

        public ComponentState State
        {
            get { return state; }
            set { state = value; }
        }

        public object UserData
        {
            get { return userData; }
            set { userData = value; }
        }
        
        public int CountChildren
        {
            get { return RectTransform.CountChildren; }
        }

        public virtual Color Color
        {
            get { return color; }
            set { color = value; }
        }

        public virtual Color HoverColor
        {
            get { return hoverColor; }
            set { hoverColor = value; }
        }

        public virtual Color SelectedColor
        {
            get { return selectedColor; }
            set { selectedColor = value; }
        }

        public virtual Color PressedColor
        {
            get { return pressedColor; }
            set { pressedColor = value; }
        }

        private RectTransform rectTransform;
        public RectTransform RectTransform
        {
            get { return rectTransform; }
            private set
            {
                rectTransform = value;
                // This is the only place where the element should be assigned!
                if (rectTransform != null)
                {
                    rectTransform.GUIComponent = this;
                }
            }
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        protected GUIComponent(string style, RectTransform rectT) : this(style)
        {
            RectTransform = rectT;
        }

        protected GUIComponent(string style)
        {
            Visible = true;

            TileSprites = true;

            OutlineColor = Color.Transparent;

            Font = GUI.Font;

            CanBeFocused = true;

            if (style != null)
                GUI.Style.Apply(this, style);
        }

        #region Updating
        public virtual void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            if (!Visible) return;

            UpdateOrder = order;
            GUI.AddToUpdateList(this);
            if (!ignoreChildren)
            {
                RectTransform.AddChildrenToGUIUpdateList(ignoreChildren, order);
            }
            OnAddedToGUIUpdateList?.Invoke(this);
        }

        public void RemoveFromGUIUpdateList(bool alsoChildren = true)
        {
            GUI.RemoveFromUpdateList(this, alsoChildren);
        }

        /// <summary>
        /// Only GUI should call this method. Auto updating follows the order of GUI update list. This order can be tweaked by changing the UpdateOrder property.
        /// </summary>
        public void UpdateAuto(float deltaTime)
        {
            if (AutoUpdate)
            {
                Update(deltaTime);
            }
        }

        /// <summary>
        /// By default, all the gui elements are updated automatically in the same order they appear on the update list. 
        /// </summary>
        public void UpdateManually(float deltaTime, bool alsoChildren = false, bool recursive = true)
        {
            if (!Visible) return;

            AutoUpdate = false;
            Update(deltaTime);
            if (alsoChildren)
            {
                UpdateChildren(deltaTime, recursive);
            }
        }

        protected virtual void Update(float deltaTime)
        {
            if (!Visible) return;
            if (flashTimer > 0.0f)
            {
                flashTimer -= deltaTime;
            }
        }

        /// <summary>
        /// Updates all the children manually.
        /// </summary>
        public void UpdateChildren(float deltaTime, bool recursive)
        {
            RectTransform.Children.ForEach(c => c.GUIComponent.UpdateManually(deltaTime, recursive, recursive));
        }
        #endregion

        #region Drawing
        /// <summary>
        /// Only GUI should call this method. Auto drawing follows the order of GUI update list. This order can be tweaked by changing the UpdateOrder property.
        /// </summary>
        public void DrawAuto(SpriteBatch spriteBatch)
        {
            if (AutoDraw)
            {
                Draw(spriteBatch);
            }
        }

        /// <summary>
        /// By default, all the gui elements are drawn automatically in the same order they appear on the update list.
        /// </summary>
        public virtual void DrawManually(SpriteBatch spriteBatch, bool alsoChildren = false, bool recursive = true)
        {
            if (!Visible) return;

            AutoDraw = false;
            Draw(spriteBatch);
            if (alsoChildren)
            {
                DrawChildren(spriteBatch, recursive);
            }
        }

        /// <summary>
        /// Draws all the children manually.
        /// </summary>
        public virtual void DrawChildren(SpriteBatch spriteBatch, bool recursive)
        {
            RectTransform.Children.ForEach(c => c.GUIComponent.DrawManually(spriteBatch, recursive, recursive));
        }

        protected virtual Color GetCurrentColor(ComponentState state)
        {
            switch (state)
            {
                case ComponentState.Hover:
                    return HoverColor;
                case ComponentState.Pressed:
                    return PressedColor;
                case ComponentState.Selected:
                    return SelectedColor;
                default:
                    return Color;
            }
        }

        protected virtual void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;
            var rect = Rect;
            
            Color currColor = GetCurrentColor(state);
            if (currColor.A > 0.0f && (sprites == null || !sprites.Any())) GUI.DrawRectangle(spriteBatch, rect, currColor * (currColor.A / 255.0f), true);

            if (sprites != null && sprites[state] != null && currColor.A > 0.0f)
            {
                foreach (UISprite uiSprite in sprites[state])
                {
                    uiSprite.Draw(spriteBatch, rect, currColor * (currColor.A / 255.0f), SpriteEffects);
                }
            }

            if (flashTimer > 0.0f)
            {
                //the number of flashes depends on the duration, 1 flash per 1 full second
                int flashCycleCount = (int)Math.Max(flashDuration, 1);
                float flashCycleDuration = flashDuration / flashCycleCount;

                //MathHelper.Pi * 0.8f -> the curve goes from 144 deg to 0, 
                //i.e. quickly bumps up from almost full brightness to full and then fades out
                GUI.UIGlow.Draw(spriteBatch,
                    rect,
                    flashColor * (float)Math.Sin(flashTimer % flashCycleDuration / flashCycleDuration * MathHelper.Pi * 0.8f));
            }
        }

        /// <summary>
        /// Creates and draws a tooltip.
        /// </summary>
        public void DrawToolTip(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            DrawToolTip(spriteBatch, ToolTip, GUI.MouseOn.Rect);
        }

        public static void DrawToolTip(SpriteBatch spriteBatch, string toolTip, Rectangle targetElement)
        {
            int width = 400;
            if (toolTipBlock == null || (string)toolTipBlock.userData != toolTip)
            {
                toolTipBlock = new GUITextBlock(new RectTransform(new Point(width, 18), null), toolTip, font: GUI.SmallFont, wrap: true, style: "GUIToolTip");
                toolTipBlock.RectTransform.NonScaledSize = new Point(
                    (int)(GUI.SmallFont.MeasureString(toolTipBlock.WrappedText).X + 20),
                    toolTipBlock.WrappedText.Split('\n').Length * 18 + 7);
                toolTipBlock.userData = toolTip;
            }

            toolTipBlock.RectTransform.AbsoluteOffset = new Point(targetElement.Center.X, targetElement.Bottom);
            if (toolTipBlock.Rect.Right > GameMain.GraphicsWidth - 10)
            {
                toolTipBlock.RectTransform.AbsoluteOffset -= new Point(toolTipBlock.Rect.Width, 0);
            }
            if (toolTipBlock.Rect.Bottom > GameMain.GraphicsHeight - 10)
            {
                toolTipBlock.RectTransform.AbsoluteOffset -= new Point(
                    (targetElement.Width / 2) * Math.Sign(targetElement.Center.X - toolTipBlock.Center.X), 
                    toolTipBlock.Rect.Bottom - (GameMain.GraphicsHeight - 10));
            }
            toolTipBlock.SetTextPos();

            toolTipBlock.DrawManually(spriteBatch);
        }
        #endregion

        protected virtual void SetAlpha(float a)
        {
            color = new Color(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, a);
        }

        public virtual void Flash(Color? color = null, float flashDuration = 1.5f)
        {
            flashTimer = flashDuration;
            this.flashDuration = flashDuration;
            flashColor = (color == null) ? Color.Red : (Color)color;
        }

        public void FadeOut(float duration, bool removeAfter)
        {
            CoroutineManager.StartCoroutine(LerpAlpha(0.0f, duration, removeAfter));
        }

        private IEnumerable<object> LerpAlpha(float to, float duration, bool removeAfter)
        {
            float t = 0.0f;
            float startA = color.A;

            while (t < duration)
            {
                t += CoroutineManager.DeltaTime;

                SetAlpha(MathHelper.Lerp(startA, to, t / duration));

                yield return CoroutineStatus.Running;
            }

            SetAlpha(to);

            if (removeAfter && Parent != null)
            {
                Parent.RemoveChild(this);
            }

            yield return CoroutineStatus.Success;
        }

        public virtual void ApplyStyle(GUIComponentStyle style)
        {
            if (style == null) return;

            color = style.Color;
            hoverColor = style.HoverColor;
            selectedColor = style.SelectedColor;
            pressedColor = style.PressedColor;
            
            sprites = style.Sprites;

            OutlineColor = style.OutlineColor;

            this.style = style;
        }
    }
}
