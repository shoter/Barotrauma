﻿using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class SalvageMission : Mission
    {
        private ItemPrefab itemPrefab;

        private Item item;

        private Level.PositionType spawnPositionType;

        private int state;

        public override Vector2 SonarPosition
        {
            get
            {
                return state > 0 ? Vector2.Zero : ConvertUnits.ToDisplayUnits(item.SimPosition);
            }
        }

        public SalvageMission(MissionPrefab prefab, Location[] locations)
            : base(prefab, locations)
        {
            if (prefab.ConfigElement.Attribute("itemname") != null)
            {
                DebugConsole.ThrowError("Error in SalvageMission - use item identifier instead of the name of the item.");
                string itemName = prefab.ConfigElement.GetAttributeString("itemname", "");
                itemPrefab = MapEntityPrefab.Find(itemName) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Error in SalvageMission: couldn't find an item prefab with the name " + itemName);
                }
            }
            else
            {
                string itemIdentifier = prefab.ConfigElement.GetAttributeString("itemidentifier", "");
                itemPrefab = MapEntityPrefab.Find(null, itemIdentifier) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Error in SalvageMission - couldn't find an item prefab with the identifier " + itemIdentifier);
                }
            }

            string spawnPositionTypeStr = prefab.ConfigElement.GetAttributeString("spawntype", "");
            if (string.IsNullOrWhiteSpace(spawnPositionTypeStr) ||
                !Enum.TryParse(spawnPositionTypeStr, true, out spawnPositionType))
            {
                spawnPositionType = Level.PositionType.Cave | Level.PositionType.Ruin;
            }
        }

        public override void Start(Level level)
        {
            //ruin items are allowed to spawn close to the sub
            float minDistance = spawnPositionType == Level.PositionType.Ruin ? 0.0f : Level.Loaded.Size.X * 0.3f;
            Vector2 position = Level.Loaded.GetRandomItemPos(spawnPositionType, 100.0f, minDistance, 30.0f);
            
            item = new Item(itemPrefab, position, null);
            item.body.FarseerBody.IsKinematic = true;

            if (item.HasTag("alien"))
            {
                //try to find a nearby artifact holder (or any alien itemcontainer) and place the artifact inside it
                foreach (Item it in Item.ItemList)
                {
                    if (it.Submarine != null || !it.HasTag("alien")) continue;

                    if (Math.Abs(item.WorldPosition.X - it.WorldPosition.X) > 2000.0f) continue;
                    if (Math.Abs(item.WorldPosition.Y - it.WorldPosition.Y) > 2000.0f) continue;

                    var itemContainer = it.GetComponent<Items.Components.ItemContainer>();
                    if (itemContainer == null) continue;

                    itemContainer.Combine(item);
                    break;
                }
            }
        }

        public override void Update(float deltaTime)
        {
            switch (state)
            {
                case 0:
                    //item.body.LinearVelocity = Vector2.Zero;
                    if (item.ParentInventory != null) item.body.FarseerBody.IsKinematic = false;
                    if (item.CurrentHull?.Submarine == null) return;

#if CLIENT
                    ShowMessage(state);
#endif
                    state = 1;
                    break;
                case 1:
                    if (!Submarine.MainSub.AtEndPosition && !Submarine.MainSub.AtStartPosition) return;
#if CLIENT
                    ShowMessage(state);
#endif
                    state = 2;
                    break;
            }    
        }

        public override void End()
        {
            if (item.CurrentHull?.Submarine == null || !item.CurrentHull.Submarine.AtEndPosition || item.Removed) return;
            item.Remove();

            GiveReward();

            completed = true;
        }
    }
}
