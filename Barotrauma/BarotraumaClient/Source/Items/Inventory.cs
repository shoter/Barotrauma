﻿using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class InventorySlot
    {
        public Rectangle Rect;

        public Rectangle InteractRect;

        public bool Disabled;

        public GUIComponent.ComponentState State;
        
        public Vector2 DrawOffset;
        
        public Color Color;

        public Color HighlightColor;
        private CoroutineHandle highlightCoroutine;
        public float HighlightTimer;
        
        public Sprite SlotSprite;

        public Keys QuickUseKey;

        public int SubInventoryDir = -1;
        
        public bool IsHighlighted
        {
            get
            {
                return State == GUIComponent.ComponentState.Hover;
            }
        }

        public float QuickUseTimer;
        public string QuickUseButtonToolTip;

        public GUIComponent.ComponentState EquipButtonState;
        public Rectangle EquipButtonRect
        {
            get
            {
                int buttonDir = Math.Sign(SubInventoryDir);

                Vector2 equipIndicatorPos = new Vector2(
                    Rect.Center.X - Inventory.EquipIndicator.size.X / 2 * Inventory.UIScale,
                    Rect.Center.Y + (Rect.Height / 2 + 25 * Inventory.UIScale) * buttonDir - Inventory.EquipIndicator.size.Y / 2 * Inventory.UIScale);
                equipIndicatorPos += DrawOffset;

                return new Rectangle(
                    (int)(equipIndicatorPos.X), (int)(equipIndicatorPos.Y),
                    (int)(Inventory.EquipIndicator.size.X * Inventory.UIScale), (int)(Inventory.EquipIndicator.size.Y * Inventory.UIScale));
            }
        }

        public InventorySlot(Rectangle rect)
        {
            Rect = rect;
            InteractRect = rect;
            InteractRect.Inflate(5, 5);
            State = GUIComponent.ComponentState.None;
            Color = Color.White * 0.4f;
        }

        public bool MouseOn()
        {
            Rectangle rect = InteractRect;
            rect.Location += DrawOffset.ToPoint();
            return rect.Contains(PlayerInput.MousePosition);
        }

        public void ShowBorderHighlight(Color color, float fadeInDuration, float fadeOutDuration)
        {
            if (highlightCoroutine != null)
            {
                CoroutineManager.StopCoroutines(highlightCoroutine);
                highlightCoroutine = null;
            }

            highlightCoroutine = CoroutineManager.StartCoroutine(UpdateBorderHighlight(color, fadeInDuration, fadeOutDuration));
        }

        private IEnumerable<object> UpdateBorderHighlight(Color color, float fadeInDuration, float fadeOutDuration)
        {
            float t = 0.0f;
            HighlightTimer = 1.0f;
            while (t < fadeInDuration + fadeOutDuration)
            {
                HighlightColor = (t < fadeInDuration) ?
                    Color.Lerp(Color.Transparent, color, t / fadeInDuration) :
                    Color.Lerp(color, Color.Transparent, (t - fadeInDuration) / fadeOutDuration);

                t += CoroutineManager.DeltaTime;
                HighlightTimer = 1.0f - t / (fadeInDuration + fadeOutDuration);

                yield return CoroutineStatus.Running;
            }

            HighlightTimer = 0.0f;
            HighlightColor = Color.Transparent;

            yield return CoroutineStatus.Success;
        }
    }

    partial class Inventory
    {
        public static float UIScale
        {
            get { return (GameMain.GraphicsWidth / 1920.0f + GameMain.GraphicsHeight / 1080.0f) / 2.5f * GameSettings.InventoryScale; }
        }

        public static int ContainedIndicatorHeight
        {
            get { return (int)(15 * GameSettings.InventoryScale); }
        }

        protected float prevUIScale = UIScale;

        protected static Sprite slotSpriteSmall, slotSpriteHorizontal, slotSpriteVertical, slotSpriteRound;
        public static Sprite EquipIndicator, EquipIndicatorHighlight;
        public static Sprite DropIndicator, DropIndicatorHighlight;

        public Rectangle BackgroundFrame { get; protected set; }

        public float HideTimer;

        private bool isSubInventory;

        public class SlotReference
        {
            public readonly Inventory ParentInventory;
            public readonly InventorySlot Slot;
            public readonly int SlotIndex;

            public Inventory Inventory;

            public bool IsSubSlot;

            public SlotReference(Inventory parentInventory, InventorySlot slot, int slotIndex, bool isSubSlot, Inventory subInventory = null)
            {


                ParentInventory = parentInventory;
                Slot = slot;
                SlotIndex = slotIndex;
                Inventory = subInventory;
                IsSubSlot = isSubSlot;
            }
        }

        public static InventorySlot draggingSlot;
        public static Item draggingItem;

        public static Item doubleClickedItem;

        protected Vector4 padding;

        private int slotsPerRow;
        public int SlotsPerRow
        {
            set { slotsPerRow = Math.Max(1, value); }
        }

        protected static HashSet<SlotReference> highlightedSubInventorySlots = new HashSet<SlotReference>();

        protected static SlotReference selectedSlot;

        public InventorySlot[] slots;

        private Rectangle prevRect;
        /// <summary>
        /// If set, the inventory is automatically positioned inside the rect
        /// </summary>
        public RectTransform RectTransform;
        
        public static SlotReference SelectedSlot
        {
            get { return selectedSlot; }
        }
                
        public virtual void CreateSlots()
        {
            slots = new InventorySlot[capacity];

            int rows = (int)Math.Ceiling((double)capacity / slotsPerRow);
            int columns = Math.Min(slotsPerRow, capacity);

            Vector2 spacing = new Vector2(10 * UIScale, (10 + EquipIndicator.size.Y) * UIScale);
            Vector2 rectSize = new Vector2(60.0f * UIScale);

            //y is larger to give more space for the header
            padding = new Vector4(spacing.X, 40 * UIScale, spacing.X, spacing.X);

            Vector2 slotAreaSize = new Vector2(
                columns * rectSize.X + (columns - 1) * spacing.X,
                rows * rectSize.Y + (rows - 1) * spacing.Y);
            slotAreaSize.X += padding.X + padding.Z;
            slotAreaSize.Y += padding.Y + padding.W;

            Vector2 topLeft = new Vector2(
                GameMain.GraphicsWidth / 2 - slotAreaSize.X / 2,
                GameMain.GraphicsHeight / 2 - slotAreaSize.Y / 2);

            if (RectTransform != null)
            {
                Vector2 scale = new Vector2(
                    RectTransform.Rect.Width / slotAreaSize.X,
                    RectTransform.Rect.Height / slotAreaSize.Y);

                spacing *= scale;
                rectSize *= scale;
                padding.X *= scale.X; padding.Z *= scale.X;
                padding.Y *= scale.Y; padding.W *= scale.Y;

                topLeft = RectTransform.TopLeft.ToVector2() + new Vector2(padding.X, padding.Y);
                prevRect = RectTransform.Rect;
            }

            Rectangle slotRect = new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)rectSize.X, (int)rectSize.Y);
            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = (int)(topLeft.X + (rectSize.X + spacing.X) * (i % slotsPerRow));
                slotRect.Y = (int)(topLeft.Y + (rectSize.Y + spacing.Y) * ((int)Math.Floor((double)i / slotsPerRow)));
                slots[i] = new InventorySlot(slotRect);
                slots[i].InteractRect = new Rectangle(
                    (int)(slots[i].Rect.X - spacing.X / 2 - 1), (int)(slots[i].Rect.Y - spacing.Y / 2 - 1), 
                    (int)(slots[i].Rect.Width + spacing.X + 2), (int)(slots[i].Rect.Height + spacing.Y + 2));
            }

            if (selectedSlot != null && selectedSlot.ParentInventory == this)
            {
                selectedSlot = new SlotReference(this, slots[selectedSlot.SlotIndex], selectedSlot.SlotIndex, selectedSlot.IsSubSlot, selectedSlot.Inventory);
            }
            CalculateBackgroundFrame();
        }

        protected virtual void CalculateBackgroundFrame()
        {
        }

        protected virtual bool HideSlot(int i)
        {
            return slots[i].Disabled || (hideEmptySlot[i] && Items[i] == null);
        }

        public virtual void Update(float deltaTime, Camera cam, bool subInventory = false)
        {
            syncItemsDelay = Math.Max(syncItemsDelay - deltaTime, 0.0f);

            if (slots == null || isSubInventory != subInventory || 
                (RectTransform != null && RectTransform.Rect != prevRect))
            {
                CreateSlots();
                isSubInventory = subInventory;
            }

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;
                UpdateSlot(slots[i], i, Items[i], subInventory);
            }
            if (!isSubInventory)
            {
                ControlInput(cam);
            }
        }

        protected virtual void ControlInput(Camera cam)
        {
            // Note that these targets are static. Therefore the outcome is the same if this method is called multiple times or only once.
            if (selectedSlot != null || draggingItem != null)
            {
                cam.Freeze = true;
            }
        }

        protected void UpdateSlot(InventorySlot slot, int slotIndex, Item item, bool isSubSlot)
        {
            Rectangle interactRect = slot.InteractRect;
            interactRect.Location += slot.DrawOffset.ToPoint();

            bool mouseOnGUI = false;
            if (GUI.MouseOn != null)
            {
                //block usage if the mouse is on a GUIComponent that's not related to this inventory
                if (RectTransform == null || (RectTransform != GUI.MouseOn.RectTransform && !GUI.MouseOn.IsParentOf(RectTransform.GUIComponent)))
                {
                    mouseOnGUI = true;
                }
            }

            bool mouseOn = interactRect.Contains(PlayerInput.MousePosition) && !Locked && !mouseOnGUI;

            if (selectedSlot != null && selectedSlot.Slot != slot)
            {
                //subinventory slot highlighted -> don't allow highlighting this one
                if (selectedSlot.IsSubSlot && !isSubSlot)
                {
                    mouseOn = false;
                }
                else if (!selectedSlot.IsSubSlot && isSubSlot && mouseOn)
                {
                    selectedSlot = null;
                }
            }

            
            slot.State = GUIComponent.ComponentState.None;
            
            if (mouseOn && (draggingItem != null || selectedSlot == null || selectedSlot.Slot == slot))  
                // &&
                //(highlightedSubInventories.Count == 0 || highlightedSubInventories.Contains(this) || highlightedSubInventorySlot?.Slot == slot || highlightedSubInventory.Owner == item))
            {
                slot.State = GUIComponent.ComponentState.Hover;

                if (selectedSlot == null || (!selectedSlot.IsSubSlot && isSubSlot))
                {
                    selectedSlot = new SlotReference(this, slot, slotIndex, isSubSlot, Items[slotIndex]?.GetComponent<ItemContainer>()?.Inventory);
                }

                if (draggingItem == null)
                {
                    if (PlayerInput.LeftButtonHeld())
                    {
                        draggingItem = Items[slotIndex];
                        draggingSlot = slot;
                    }
                }
                else if (PlayerInput.LeftButtonReleased())
                {
                    if (PlayerInput.DoubleClicked())
                    {
                        doubleClickedItem = item;
                    }
                }               
            }
        }

        protected Inventory GetSubInventory(int slotIndex)
        {
            var item = Items[slotIndex];
            if (item == null) return null;

            var container = item.GetComponent<ItemContainer>();
            if (container == null) return null;

            return container.Inventory;
        }

        float openState;

        public void UpdateSubInventory(float deltaTime, int slotIndex, Camera cam)
        {
            var item = Items[slotIndex];
            if (item == null) return;

            var container = item.GetComponent<ItemContainer>();
            if (container == null || !container.DrawInventory) return;

            var subInventory = container.Inventory;            
            if (subInventory.slots == null) subInventory.CreateSlots();

            int itemCapacity = subInventory.Items.Length;
            var slot = slots[slotIndex];
            int dir = slot.SubInventoryDir;
            if (itemCapacity == 1 && false)
            {
                Point slotSize = (slotSpriteRound.size * UIScale).ToPoint();
                subInventory.slots[0].Rect = 
                    new Rectangle(slot.Rect.Center.X - slotSize.X / 2, dir > 0 ? slot.Rect.Bottom + 5 : slot.EquipButtonRect.Bottom + 5, slotSize.X, slotSize.Y);

                subInventory.slots[0].InteractRect = subInventory.slots[0].Rect;
                subInventory.slots[0].DrawOffset = slot.DrawOffset;
            }
            else
            {
                Rectangle subRect = slot.Rect;
                subRect.Width = slots[slotIndex].SlotSprite == null ? (int)(60 * UIScale) : (int)(slots[slotIndex].SlotSprite.size.X * UIScale);
                subRect.Height = (int)(60 * UIScale);

                Vector2 spacing = new Vector2(10 * UIScale, (10 + EquipIndicator.size.Y) * UIScale);

                int columns = (int)Math.Max(Math.Floor(Math.Sqrt(itemCapacity)), 1);
                while (itemCapacity / columns * (subRect.Height + spacing.Y) > GameMain.GraphicsHeight * 0.5f)
                {
                    columns++;
                }

                int startX = slot.Rect.Center.X - (int)(subRect.Width * (columns / 2.0f) + spacing.X * ((columns - 1) / 2.0f));
                subRect.X = startX;
                int startY = dir < 0 ?
                    slot.EquipButtonRect.Y - subRect.Height - (int)(35 * UIScale) :
                    slot.EquipButtonRect.Bottom + (int)(10 * UIScale);
                subRect.Y = startY;

                float totalHeight = itemCapacity / columns * (subRect.Height + spacing.Y);
                subInventory.openState = subInventory.HideTimer >= 0.5f ?
                    Math.Min(subInventory.openState + deltaTime * 5.0f, 1.0f) :
                    Math.Max(subInventory.openState - deltaTime * 3.0f, 0.0f);

                for (int i = 0; i < itemCapacity; i++)
                { 
                    subInventory.slots[i].Rect = subRect;
                    subInventory.slots[i].Rect.Location += new Point(0, (int)totalHeight * -dir);

                    subInventory.slots[i].DrawOffset = Vector2.SmoothStep( new Vector2(0, -50 * dir), new Vector2(0, totalHeight * dir), subInventory.openState);
                    
                    subInventory.slots[i].InteractRect = new Rectangle(
                        (int)(subInventory.slots[i].Rect.X - spacing.X / 2 - 1), (int)(subInventory.slots[i].Rect.Y - spacing.Y / 2 - 1),
                        (int)(subInventory.slots[i].Rect.Width + spacing.X + 2), (int)(subInventory.slots[i].Rect.Height + spacing.Y + 2));

                    if ((i + 1) % columns == 0)
                    {
                        subRect.X = startX;
                        subRect.Y += subRect.Height * dir;
                        subRect.Y += (int)(spacing.Y * dir);
                    }
                    else
                    {
                        subRect.X = (int)(subInventory.slots[i].Rect.Right + spacing.X);
                    }
                }        
                slots[slotIndex].State = GUIComponent.ComponentState.Hover;
            }
            subInventory.isSubInventory = true;    
            subInventory.Update(deltaTime, cam, true);
        }


        public virtual void Draw(SpriteBatch spriteBatch, bool subInventory = false)
        {
            if (slots == null || isSubInventory != subInventory) return;

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;

                //don't draw the item if it's being dragged out of the slot
                bool drawItem = draggingItem == null || draggingItem != Items[i] || slots[i].InteractRect.Contains(PlayerInput.MousePosition);

                DrawSlot(spriteBatch, this, slots[i], Items[i], drawItem);
            }
        }

        /// <summary>
        /// Is the mouse on any inventory element (slot, equip button, subinventory...)
        /// </summary>
        /// <returns></returns>
        public static bool IsMouseOnInventory()
        {
            if (Character.Controlled == null) return false;

            if (draggingItem != null) return true;

            if (Character.Controlled.Inventory != null)
            {
                foreach (InventorySlot slot in Character.Controlled.Inventory.slots)
                {
                    if (slot.InteractRect.Contains(PlayerInput.MousePosition) ||
                        slot.EquipButtonRect.Contains(PlayerInput.MousePosition))
                    {
                        return true;
                    }
                }
            }
            if (Character.Controlled.SelectedCharacter?.Inventory != null)
            {
                foreach (InventorySlot slot in Character.Controlled.SelectedCharacter.Inventory.slots)
                {
                    if (slot.InteractRect.Contains(PlayerInput.MousePosition) ||
                        slot.EquipButtonRect.Contains(PlayerInput.MousePosition))
                    {
                        return true;
                    }
                }
            }

            if (Character.Controlled.SelectedConstruction != null)
            {
                foreach (ItemComponent ic in Character.Controlled.SelectedConstruction.components)
                {
                    var itemContainer = ic as ItemContainer;
                    if (itemContainer?.Inventory?.slots == null) continue;

                    foreach (InventorySlot slot in itemContainer.Inventory.slots)
                    {
                        if (slot.InteractRect.Contains(PlayerInput.MousePosition) ||
                            slot.EquipButtonRect.Contains(PlayerInput.MousePosition))
                        {
                            return true;
                        }
                    }
                }
            }

            foreach (SlotReference highlightedSubInventorySlot in highlightedSubInventorySlots)
            {
                if (GetSubInventoryHoverArea(highlightedSubInventorySlot).Contains(PlayerInput.MousePosition)) return true;
            }

            return false;
        }

        protected static void DrawToolTip(SpriteBatch spriteBatch, string toolTip, Rectangle highlightedSlot)
        {
            GUIComponent.DrawToolTip(spriteBatch, toolTip, highlightedSlot);
        }

        public void DrawSubInventory(SpriteBatch spriteBatch, int slotIndex)
        {
            var item = Items[slotIndex];
            if (item == null) return;

            var container = item.GetComponent<ItemContainer>();
            if (container == null || !container.DrawInventory) return;

            if (container.Inventory.slots == null || !container.Inventory.isSubInventory) return;

            int itemCapacity = container.Capacity;

#if DEBUG
            System.Diagnostics.Debug.Assert(slotIndex >= 0 && slotIndex < Items.Length);
#else
            if (slotIndex < 0 || slotIndex >= Items.Length) return;
#endif

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);
            if (slots[slotIndex].SubInventoryDir > 0)
            {
                spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(
                    new Point(0, slots[slotIndex].Rect.Bottom),
                    new Point(GameMain.GraphicsWidth, (int)Math.Max(GameMain.GraphicsHeight - slots[slotIndex].Rect.Bottom, 0)));
            }
            else
            {
                spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(
                    new Point(0, 0),
                    new Point(GameMain.GraphicsWidth, slots[slotIndex].Rect.Y));
            }
            container.Inventory.Draw(spriteBatch, true);
            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
            spriteBatch.Begin(SpriteSortMode.Deferred);

            container.InventoryBottomSprite?.Draw(spriteBatch,
                new Vector2(slots[slotIndex].Rect.Center.X, slots[slotIndex].Rect.Y) + slots[slotIndex].DrawOffset,
                0.0f, UIScale);

            container.InventoryTopSprite?.Draw(spriteBatch,
                new Vector2(
                    slots[slotIndex].Rect.Center.X, 
                    container.Inventory.slots[container.Inventory.slots.Length - 1].Rect.Y) + container.Inventory.slots[container.Inventory.slots.Length - 1].DrawOffset,
                0.0f, UIScale);

        }

        public static void UpdateDragging()
        {
            if (draggingItem != null && PlayerInput.LeftButtonReleased())
            {
                if (CharacterHealth.OpenHealthWindow != null && 
                    CharacterHealth.OpenHealthWindow.OnItemDropped(draggingItem, false))
                {
                    draggingItem = null;
                    return;
                }
                
                if (selectedSlot == null)
                {
                    draggingItem.ParentInventory?.CreateNetworkEvent();
                    draggingItem.Drop();
                    GUI.PlayUISound(GUISoundType.DropItem);
                }
                else if (selectedSlot.ParentInventory.Items[selectedSlot.SlotIndex] != draggingItem)
                {
                    Inventory selectedInventory = selectedSlot.ParentInventory;
                    int slotIndex = selectedSlot.SlotIndex;
                    if (selectedInventory.TryPutItem(draggingItem, slotIndex, true, true, Character.Controlled))
                    {
                        if (selectedInventory.slots != null) selectedInventory.slots[slotIndex].ShowBorderHighlight(Color.White, 0.1f, 0.4f);
                        GUI.PlayUISound(GUISoundType.PickItem);
                    }
                    else
                    {
                        if (selectedInventory.slots != null) selectedInventory.slots[slotIndex].ShowBorderHighlight(Color.Red, 0.1f, 0.9f);
                        GUI.PlayUISound(GUISoundType.PickItemFail);
                    }
                    selectedInventory.HideTimer = 1.0f;
                    if (selectedSlot.ParentInventory?.Owner is Item parentItem && parentItem.ParentInventory != null)
                    {
                        for (int i = 0; i < parentItem.ParentInventory.capacity; i++)
                        {
                            if (parentItem.ParentInventory.HideSlot(i)) continue;
                            if (parentItem.ParentInventory.Items[i] != parentItem) continue;

                            highlightedSubInventorySlots.Add(new SlotReference(
                                parentItem.ParentInventory, parentItem.ParentInventory.slots[i],
                                i, false, selectedSlot.ParentInventory));
                            break;
                        }

                    }
                    draggingItem = null;
                    draggingSlot = null;
                }

                draggingItem = null;
            }

            if (selectedSlot != null && !selectedSlot.Slot.MouseOn())
            {
                selectedSlot = null;
            }
        }
        
        protected static Rectangle GetSubInventoryHoverArea(SlotReference subSlot)
        {
            Rectangle hoverArea = subSlot.Slot.Rect;
            hoverArea.Location += subSlot.Slot.DrawOffset.ToPoint();
            hoverArea = Rectangle.Union(hoverArea, subSlot.Slot.EquipButtonRect);
            if (subSlot.Inventory?.slots != null)
            {
                foreach (InventorySlot slot in subSlot.Inventory.slots)
                {
                    Rectangle subSlotRect = slot.InteractRect;
                    subSlotRect.Location += slot.DrawOffset.ToPoint();
                    hoverArea = Rectangle.Union(hoverArea, subSlotRect);
                }
                if (subSlot.Slot.SubInventoryDir < 0)
                {
                    hoverArea.Height -= hoverArea.Bottom - subSlot.Slot.Rect.Bottom;
                }
                else
                {
                    int over = subSlot.Slot.Rect.Y - hoverArea.Y;
                    hoverArea.Y += over;
                    hoverArea.Height -= over;
                }
            }
            hoverArea.Inflate(10, 10);
            return hoverArea;
        }

        public static void DrawFront(SpriteBatch spriteBatch)
        {
            foreach (var slot in highlightedSubInventorySlots)
            {
                int slotIndex = Array.IndexOf(slot.ParentInventory.slots, slot.Slot);
                if (slotIndex > 0 && slotIndex < slot.ParentInventory.slots.Length)
                {
                    slot.ParentInventory.DrawSubInventory(spriteBatch, slotIndex);
                }
            }

            if (draggingItem != null)
            {
                if (draggingSlot == null || (!draggingSlot.MouseOn()))
                {
                    Sprite sprite = draggingItem.Prefab.InventoryIcon ?? draggingItem.Sprite;

                    int iconSize = (int)(64 * GUI.Scale);
                    float scale = Math.Min(Math.Min(iconSize / sprite.size.X, iconSize / sprite.size.Y), 1.5f);
                    Vector2 itemPos = PlayerInput.MousePosition;
                    sprite.Draw(spriteBatch, itemPos + Vector2.One * 2, Color.Black, scale: scale);
                    sprite.Draw(spriteBatch, 
                        itemPos, 
                        sprite == draggingItem.Sprite ? draggingItem.GetSpriteColor() : draggingItem.GetInventoryIconColor(), 
                        scale: scale);
                }
            }

            if (selectedSlot != null)
            {
                Item item = selectedSlot.ParentInventory.Items[selectedSlot.SlotIndex];
                if (item != null)
                {
                    string toolTip = "";
                    if (GameMain.DebugDraw)
                    {
                        toolTip = item.ToString();
                    }
                    else
                    {
                        string description = item.Description;
                        if (item.Prefab.Identifier == "idcard")
                        {
                            string[] readTags = item.Tags.Split(',');
                            string idName = null;
                            string idJob = null;
                            foreach (string tag in readTags)
                            {
                                string[] s = tag.Split(':');
                                if (s[0] == "name")
                                    idName = s[1];
                                if (s[0] == "job")
                                    idJob = s[1];
                            }
                            if (idName != null)
                            {
                                if (idJob == null)
                                {
                                    description = TextManager.Get("IDCardName").Replace("[name]", idName);
                                }
                                else
                                {
                                    description = TextManager.Get("IDCardNameJob").Replace("[name]", idName).Replace("[job]", idJob);
                                }
                                if (!string.IsNullOrEmpty(item.Description))
                                {
                                    description = description + " " + item.Description;
                                }
                            }
                        }
                        toolTip = string.IsNullOrEmpty(description) ?
                            item.Name :
                            item.Name + '\n' + description;
                    }

                    Rectangle slotRect = selectedSlot.Slot.Rect;
                    slotRect.Location += selectedSlot.Slot.DrawOffset.ToPoint();
                    DrawToolTip(spriteBatch, toolTip, slotRect);
                }
            }
        }

        public static void DrawSlot(SpriteBatch spriteBatch, Inventory inventory, InventorySlot slot, Item item, bool drawItem = true)
        {
            Rectangle rect = slot.Rect;
            rect.Location += slot.DrawOffset.ToPoint();
            
            if (slot.HighlightColor.A > 0)
            {
                float scaleUpAmount = 0.5f;
                float inflateAmount = (slot.HighlightColor.A / 255.0f) * scaleUpAmount * 0.5f;
                rect.Inflate(rect.Width * inflateAmount, rect.Height * inflateAmount);
            }

            var itemContainer = item?.GetComponent<ItemContainer>();
            if (itemContainer != null && (itemContainer.InventoryTopSprite != null || itemContainer.InventoryBottomSprite != null))
            {
                if (!highlightedSubInventorySlots.Any(s => s.Slot == slot))
                {
                    itemContainer.InventoryBottomSprite?.Draw(spriteBatch, new Vector2(rect.Center.X, rect.Y), 0, UIScale);
                    itemContainer.InventoryTopSprite?.Draw(spriteBatch, new Vector2(rect.Center.X, rect.Y), 0, UIScale);
                }

                drawItem = false;
            }
            else
            {
                Sprite slotSprite = slot.SlotSprite ?? slotSpriteSmall;

                spriteBatch.Draw(slotSprite.Texture, rect, slotSprite.SourceRect, slot.IsHighlighted ? Color.White : Color.White * 0.8f);

                if (item != null && drawItem)
                {
                    if (item.Condition < item.Prefab.Health && (itemContainer == null || !itemContainer.ShowConditionInContainedStateIndicator))
                    {
                        GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Bottom - 8, rect.Width, 8), Color.Black * 0.8f, true);
                        GUI.DrawRectangle(spriteBatch,
                            new Rectangle(rect.X, rect.Bottom - 8, (int)(rect.Width * item.Condition / item.Prefab.Health), 8),
                            Color.Lerp(Color.Red, Color.Green, item.Condition / 100.0f) * 0.8f, true);
                    }

                    if (itemContainer != null)
                    {
                        float containedState = 0.0f;
                        if (itemContainer.ShowConditionInContainedStateIndicator)
                        {
                            containedState = item.Condition / 100.0f;
                        }
                        else
                        {
                            containedState = itemContainer.Inventory.Capacity == 1 ?
                                (itemContainer.Inventory.Items[0] == null ? 0.0f : itemContainer.Inventory.Items[0].Condition / 100.0f) :
                                itemContainer.Inventory.Items.Count(i => i != null) / (float)itemContainer.Inventory.capacity;
                        }

                        int dir = slot.SubInventoryDir;
                        Rectangle containedIndicatorArea = new Rectangle(rect.X,
                            dir < 0 ? rect.Bottom + HUDLayoutSettings.Padding / 2 : rect.Y - HUDLayoutSettings.Padding / 2 - ContainedIndicatorHeight, rect.Width, ContainedIndicatorHeight);
                        containedIndicatorArea.Inflate(-4, 0);
                                                
                        if (itemContainer.ContainedStateIndicator?.Texture == null)
                        {
                            containedIndicatorArea.Inflate(0, -2);
                            GUI.DrawRectangle(spriteBatch, containedIndicatorArea, Color.DarkGray * 0.8f, true);
                            GUI.DrawRectangle(spriteBatch,
                                new Rectangle(containedIndicatorArea.X, containedIndicatorArea.Y, (int)(containedIndicatorArea.Width * containedState), containedIndicatorArea.Height),
                                Color.Lerp(Color.Red, Color.Green, containedState) * 0.8f, true);
                        }
                        else
                        {
                            Sprite indicatorSprite = itemContainer.ContainedStateIndicator;
                            float indicatorScale = Math.Min(
                                containedIndicatorArea.Width / (float)indicatorSprite.SourceRect.Width,
                                containedIndicatorArea.Height / (float)indicatorSprite.SourceRect.Height);

                            if (containedState > 0.0f && containedState < 0.25f)
                            {
                                indicatorScale += ((float)Math.Sin(Timing.TotalTime * 5.0f) + 1.0f) * 0.1f;
                            }

                            indicatorSprite.Draw(spriteBatch, containedIndicatorArea.Center.ToVector2(),
                                Color.DarkGray * 0.6f, 
                                origin: indicatorSprite.size / 2,
                                rotate: 0.0f,
                                scale: indicatorScale);
                     
                            spriteBatch.Draw(indicatorSprite.Texture, containedIndicatorArea.Center.ToVector2(),
                                sourceRectangle: new Rectangle(indicatorSprite.SourceRect.Location, new Point((int)(indicatorSprite.SourceRect.Width * containedState), indicatorSprite.SourceRect.Height)),
                                color: ToolBox.GradientLerp(containedState, Color.Red, Color.Orange, Color.Green),
                                rotation: 0.0f,
                                origin: indicatorSprite.size / 2,
                                scale: indicatorScale,
                                effects: SpriteEffects.None, layerDepth: 0.0f);
                        }
                    }
                }
            }

            if (GameMain.DebugDraw)
            {
                GUI.DrawRectangle(spriteBatch, rect, Color.White, false, 0, 1);
                GUI.DrawRectangle(spriteBatch, slot.EquipButtonRect, Color.White, false, 0, 1);
            }

            if (slot.HighlightColor != Color.Transparent)
            {
                GUI.UIGlow.Draw(spriteBatch, rect, slot.HighlightColor);
            }

            if (item != null && drawItem)
            {
                Sprite sprite = item.Prefab.InventoryIcon ?? item.Sprite;
                float scale = Math.Min(Math.Min((rect.Width - 10) / sprite.size.X, (rect.Height - 10) / sprite.size.Y), 3.0f);
                Vector2 itemPos = rect.Center.ToVector2();
                if (itemPos.Y > GameMain.GraphicsHeight)
                {
                    itemPos.Y -= Math.Min(
                        (itemPos.Y + sprite.size.Y / 2 * scale) - GameMain.GraphicsHeight,
                        (itemPos.Y - sprite.size.Y / 2 * scale) - rect.Y);
                }

                float rotation = 0.0f;
                if (slot.HighlightColor.A > 0)
                {
                    rotation = (float)Math.Sin(slot.HighlightTimer * MathHelper.TwoPi) * slot.HighlightTimer * 0.3f;
                }

                Color spriteColor = sprite == item.Sprite ? item.GetSpriteColor() : item.GetInventoryIconColor();
                if (CharacterHealth.OpenHealthWindow != null && !item.UseInHealthInterface)
                {
                    spriteColor = Color.Lerp(spriteColor, Color.TransparentBlack, 0.5f);
                }
                else
                {
                    sprite.Draw(spriteBatch, itemPos + Vector2.One * 2, Color.Black * 0.6f, rotate: rotation, scale: scale);
                }
                sprite.Draw(spriteBatch, itemPos, spriteColor, rotation, scale);

                if (CharacterHealth.OpenHealthWindow != null)
                {
                    float treatmentSuitability = CharacterHealth.OpenHealthWindow.GetTreatmentSuitability(item);
                    float skill = Character.Controlled.GetSkillLevel("medical");
                    if (skill > 50.0f)
                    {
                        Rectangle highlightRect = rect;
                        highlightRect.Inflate(3, 3);

                        Color color = treatmentSuitability < 0.0f ?
                            Color.Lerp(Color.Transparent, Color.Red, -treatmentSuitability) :
                            Color.Lerp(Color.Transparent, Color.Green, treatmentSuitability);
                        GUI.DrawRectangle(spriteBatch, highlightRect, color * (((float)Math.Sin(Timing.TotalTime * 5.0f) + 1.0f) / 2.0f), false, 0, 5);
                    }
                }
            }

            if (inventory != null && Character.Controlled?.Inventory == inventory && slot.QuickUseKey != Keys.None)
            {
                GUI.DrawString(spriteBatch, rect.Location.ToVector2(), 
                    slot.QuickUseKey.ToString().Substring(1, 1), 
                    item == null || !drawItem ? Color.Gray : Color.White, 
                    Color.Black * 0.8f);
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            receivedItemIDs = new ushort[capacity];

            for (int i = 0; i < capacity; i++)
            {
                receivedItemIDs[i] = msg.ReadUInt16();
            }

            //delay applying the new state if less than 1 second has passed since this client last sent a state to the server
            //prevents the inventory from briefly reverting to an old state if items are moved around in quick succession

            //also delay if we're still midround syncing, some of the items in the inventory may not exist yet
            if (syncItemsDelay > 0.0f || GameMain.Client.MidRoundSyncing)
            {
                if (syncItemsCoroutine != null) CoroutineManager.StopCoroutines(syncItemsCoroutine);
                syncItemsCoroutine = CoroutineManager.StartCoroutine(SyncItemsAfterDelay());
            }
            else
            {
                if (syncItemsCoroutine != null)
                {
                    CoroutineManager.StopCoroutines(syncItemsCoroutine);
                    syncItemsCoroutine = null;
                }
                ApplyReceivedState();
            }
        }

        private IEnumerable<object> SyncItemsAfterDelay()
        {
            while (syncItemsDelay > 0.0f || (GameMain.Client != null && GameMain.Client.MidRoundSyncing))
            {
                yield return CoroutineStatus.Running;
            }

            if (Owner.Removed || GameMain.Client == null)
            {
                yield return CoroutineStatus.Success;
            }

            ApplyReceivedState();

            yield return CoroutineStatus.Success;
        }

        private void ApplyReceivedState()
        {
            if (receivedItemIDs == null) return;

            for (int i = 0; i < capacity; i++)
            {
                if (receivedItemIDs[i] == 0 || (Entity.FindEntityByID(receivedItemIDs[i]) as Item != Items[i]))
                {
                    if (Items[i] != null) Items[i].Drop();
                    System.Diagnostics.Debug.Assert(Items[i] == null);
                }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (receivedItemIDs[i] > 0)
                {
                    var item = Entity.FindEntityByID(receivedItemIDs[i]) as Item;
                    if (item == null || Items[i] == item) continue;

                    TryPutItem(item, i, true, true, null, false);
                    for (int j = 0; j < capacity; j++)
                    {
                        if (Items[j] == item && receivedItemIDs[j] != item.ID)
                        {
                            Items[j] = null;
                        }
                    }
                }
            }

            receivedItemIDs = null;
        }
    }
}
