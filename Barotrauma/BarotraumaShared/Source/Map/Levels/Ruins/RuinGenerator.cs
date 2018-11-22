﻿using FarseerPhysics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Voronoi2;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;

namespace Barotrauma.RuinGeneration
{
    abstract class RuinShape
    {
        protected Rectangle rect;

        public Rectangle Rect
        {
            get { return rect; }
        }
        
        public int DistanceFromEntrance
        {
            get;
            set;
        }

        public Vector2 Center
        {
            get { return rect.Center.ToVector2(); }
        }

        public RuinRoom RoomType;

        public List<Line> Walls;
        
        public virtual void CreateWalls() { }

        public Alignment GetLineAlignment(Line line)
        {
            if (line.IsHorizontal)
            {
                if (line.A.Y > rect.Center.Y && line.B.Y > rect.Center.Y)
                {
                    return Alignment.Bottom;
                }
                else if (line.A.Y < rect.Center.Y && line.B.Y < rect.Center.Y)
                {
                    return Alignment.Top;
                }
            }
            else
            {
                if (line.A.X < rect.Center.X && line.B.X < rect.Center.X)
                {
                    return Alignment.Left;
                }
                else if (line.A.X > rect.Center.X && line.B.X > rect.Center.X)
                {
                    return Alignment.Right;
                }
            }

            return Alignment.Center;
        }

         /// <summary>
        /// Goes through all the walls of the ruin shape and clips off parts that are inside the rectangle
        /// </summary>
        public void SplitWalls(Rectangle rectangle)
        {
            List<Line> newLines = new List<Line>();

            foreach (Line line in Walls)
            {
                if (!line.IsHorizontal) //vertical line
                {
                    //line doesn't intersect the rectangle
                    if (rectangle.X > line.A.X || rectangle.Right < line.A.X ||
                        rectangle.Y > line.B.Y || rectangle.Bottom < line.A.Y)
                    {
                        newLines.Add(line);
                    }
                    //line completely inside the rectangle, no need to create a wall at all
                    else if (line.A.Y >= rectangle.Y && line.B.Y <= rectangle.Bottom)
                    {
                        continue;
                    }
                    //point A is within the rectangle -> cut a portion from the top of the line
                    else if (line.A.Y >= rectangle.Y && line.A.Y <= rectangle.Bottom)
                    {
                        newLines.Add(new Line(new Vector2(line.A.X, rectangle.Bottom), line.B));
                    }
                    //point B is within the rectangle -> cut a portion from the bottom of the line
                    else if (line.B.Y >= rectangle.Y && line.B.Y <= rectangle.Bottom)
                    {
                        newLines.Add(new Line(line.A, new Vector2(line.A.X, rectangle.Y)));
                    }
                    //rect is in between the lines -> split the line into two
                    else
                    {
                        newLines.Add(new Line(line.A, new Vector2(line.A.X, rectangle.Y)));
                        newLines.Add(new Line(new Vector2(line.A.X, rectangle.Bottom), line.B));
                    }
                }
                else
                {
                    //line doesn't intersect the rectangle
                    if (rectangle.X > line.B.X || rectangle.Right < line.A.X ||
                        rectangle.Y > line.A.Y || rectangle.Bottom < line.A.Y)
                    {

                        newLines.Add(line);
                    }
                    else if (line.A.X >= rectangle.X && line.B.X <= rectangle.Right)
                    {
                        continue;
                    }
                    //point A is within the rectangle -> cut a portion from the left side of the line
                    else if (line.A.X >= rectangle.X && line.A.X <= rectangle.Right)
                    {
                        newLines.Add(new Line(new Vector2(rectangle.Right, line.A.Y), line.B));
                    }
                    //point B is within the rectangle -> cut a portion from the right side of the line
                    else if (line.B.X >= rectangle.X && line.B.X <= rectangle.Right)
                    {
                        newLines.Add(new Line(line.A, new Vector2(rectangle.X, line.A.Y)));
                    }
                    //rect is in between the lines -> split the line into two
                    else
                    {
                        newLines.Add(new Line(line.A, new Vector2(rectangle.X, line.A.Y)));
                        newLines.Add(new Line(new Vector2(rectangle.Right, line.A.Y), line.B));
                    }
                }
            }

            Walls = newLines;
        }

        public void MirrorX(Vector2 mirrorOrigin)
        {
            rect.X = (int)(mirrorOrigin.X + (mirrorOrigin.X - rect.Right));
            for (int i = 0; i < Walls.Count; i++)
            {
                Walls[i].A = new Vector2(mirrorOrigin.X + (mirrorOrigin.X - Walls[i].A.X), Walls[i].A.Y);
                Walls[i].B = new Vector2(mirrorOrigin.X + (mirrorOrigin.X - Walls[i].B.X), Walls[i].B.Y);

                if (Walls[i].B.X < Walls[i].A.X)
                {
                    var temp = Walls[i].A.X;
                    Walls[i].A.X = Walls[i].B.X;
                    Walls[i].B.X = temp;
                }
            }
        }
    }

    class Line
    {
        public Vector2 A, B;

        public float Radius;

        public bool IsHorizontal
        {
            get { return Math.Abs(A.Y - B.Y) < Math.Abs(A.X - B.X); }
        }

        public Line(Vector2 a, Vector2 b)
        {
            Debug.Assert(a.X <= b.X);
            Debug.Assert(a.Y <= b.Y);

            A = a;
            B = b;
        }
    }  

    partial class Ruin
    {
        private List<BTRoom> rooms;
        private List<Corridor> corridors;

        private List<Line> walls;

        private List<RuinShape> allShapes;

        private RuinGenerationParams generationParams;

        private BTRoom entranceRoom;

        public List<RuinShape> RuinShapes
        {
            get { return allShapes; }
        }

        public List<Line> Walls
        {
            get { return walls; }
        }

        public Rectangle Area
        {
            get;
            private set;
        }

        public Ruin(VoronoiCell closestPathCell, List<VoronoiCell> caveCells, Rectangle area, bool mirror = false)
        {
            generationParams = RuinGenerationParams.GetRandom();

            Area = area;
            corridors = new List<Corridor>();
            rooms = new List<BTRoom>();
            walls = new List<Line>();
            allShapes = new List<RuinShape>();
            Generate(closestPathCell, caveCells, area, mirror);
        }
             
        public void Generate(VoronoiCell closestPathCell, List<VoronoiCell> caveCells, Rectangle area, bool mirror = false)
        {
            corridors.Clear();
            rooms.Clear();
            
            int iterations = Rand.Range(generationParams.RoomDivisionIterationsMin, generationParams.RoomDivisionIterationsMax, Rand.RandSync.Server);
            float verticalProbability = generationParams.VerticalSplitProbability;

            BTRoom baseRoom = new BTRoom(area);
            rooms = new List<BTRoom> { baseRoom };

            for (int i = 0; i < iterations; i++)
            {
                rooms.ForEach(l => l.Split(0.3f, verticalProbability, generationParams.MinSplitWidth));
                rooms = baseRoom.GetLeaves();
            }

            foreach (BTRoom leaf in rooms)
            {
                leaf.Scale
                    (
                        new Vector2(
                            Rand.Range(generationParams.RoomWidthRange.X, generationParams.RoomWidthRange.Y, Rand.RandSync.Server), 
                            Rand.Range(generationParams.RoomHeightRange.X, generationParams.RoomHeightRange.Y, Rand.RandSync.Server))
                    );
            }

            baseRoom.GenerateCorridors(generationParams.CorridorWidthRange.X, generationParams.CorridorWidthRange.Y, corridors);

            walls = new List<Line>();
            rooms.ForEach(leaf => { leaf.CreateWalls(); });

            //---------------------------
            
            float shortestDistance = 0.0f;
            foreach (BTRoom leaf in rooms)
            {
                Vector2 leafPos = leaf.Rect.Center.ToVector2();
                if (mirror)
                {
                    leafPos.X = area.Center.X + (area.Center.X - leafPos.X);
                }
                float distance = Vector2.Distance(leafPos, closestPathCell.Center);
                if (entranceRoom == null || distance < shortestDistance)
                {
                    entranceRoom = leaf;
                    shortestDistance = distance;
                }
            }

            rooms.Remove(entranceRoom);

            //---------------------------

            foreach (BTRoom leaf in rooms)
            {
                foreach (Corridor corridor in corridors)
                {
                    leaf.SplitWalls(corridor.Rect);
                }

                walls.AddRange(leaf.Walls);
            }

            foreach (Corridor corridor in corridors)
            {
                corridor.CreateWalls();

                foreach (BTRoom leaf in rooms)
                {
                    corridor.SplitWalls(leaf.Rect);
                }

                foreach (Corridor corridor2 in corridors)
                {
                    if (corridor == corridor2) continue;
                    corridor.SplitWalls(corridor2.Rect);
                }
                walls.AddRange(corridor.Walls);
            }
            
            BTRoom.CalculateDistancesFromEntrance(entranceRoom, rooms, corridors);
            GenerateRuinEntities(caveCells, area, mirror);
        }

#if DEBUG
        public List<List<Rectangle>> hullSplitIter = new List<List<Rectangle>>();
#endif

        class RuinEntity
        {
            public readonly RuinEntityConfig Config;
            public readonly MapEntity Entity;
            public readonly MapEntity Parent;
            public readonly RuinShape Room;

            public RuinEntity(RuinEntityConfig config, MapEntity entity, RuinShape room, MapEntity parent = null)
            {
                Config = config;
                Entity = entity;
                Room = room;
                Parent = parent;
            }
        }
        private List<RuinEntity> ruinEntities = new List<RuinEntity>();
        private void GenerateRuinEntities(List<VoronoiCell> caveCells, Rectangle ruinArea, bool mirror)
        {
            var entityGrid = Hull.GenerateEntityGrid(new Rectangle(ruinArea.X, ruinArea.Y + ruinArea.Height, ruinArea.Width, ruinArea.Height));

            allShapes = new List<RuinShape>(rooms);
            allShapes.AddRange(corridors);

            if (mirror)
            {
                foreach (RuinShape shape in allShapes)
                {
                    shape.MirrorX(ruinArea.Center.ToVector2());
                }
            }

            int maxRoomDistanceFromEntrance = rooms.Max(s => s.DistanceFromEntrance);
            int maxCorridorDistanceFromEntrance = corridors.Max(s => s.DistanceFromEntrance);

            //assign the room types for the first and last rooms
            foreach (RuinRoom roomType in generationParams.RoomTypeList)
            {
                RuinShape selectedRoom = null;
                switch (roomType.Placement)
                {
                    case RuinRoom.RoomPlacement.First:
                        //find the room nearest to the entrance
                        //there may be multiple ones, choose one that hasn't been assigned yet
                        selectedRoom = roomType.IsCorridor ? FindFirstRoom(corridors, r => r.RoomType == null) : FindFirstRoom(rooms, r => r.RoomType == null);

                        break;
                    case RuinRoom.RoomPlacement.Last:
                        //find the room furthest to the entrance
                        //there may be multiple ones, choose one that hasn't been assigned yet
                        selectedRoom = roomType.IsCorridor ? FindLastRoom(corridors, r => r.RoomType == null) : FindLastRoom(rooms, r => r.RoomType == null);
                        break;
                }
                if (selectedRoom == null) continue;

                //step forwards/backwards from the selected room according to the placement offset
                for (int i = 0; i < Math.Abs(roomType.PlacementOffset); i++)
                {
                    selectedRoom = FindNearestRoom(
                        selectedRoom, 
                        roomType.IsCorridor ? corridors : (IEnumerable<RuinShape>)rooms, 
                        roomType.PlacementOffset,
                        r => r.RoomType == null);
                }

                if (selectedRoom != null) selectedRoom.RoomType = roomType;
            }

            //go through the unassigned rooms
            foreach (RuinShape room in allShapes)
            {
                if (room.RoomType != null) continue;
                
                room.RoomType = generationParams.RoomTypeList.GetRandom(rt =>
                    rt.IsCorridor == room is Corridor &&
                    rt.Placement == RuinRoom.RoomPlacement.Any,
                    Rand.RandSync.Server);

                if (room.RoomType == null)
                {
                    DebugConsole.ThrowError("Could not find a suitable room type for a room (is corridor: " + (room is Corridor) + ")");
                }                
            }

            List<Rectangle> hullRects = new List<Rectangle>(allShapes.Select(s => s.Rect));
            List<Door> doors = new List<Door>();

#if DEBUG
            hullSplitIter.Add(new List<Rectangle>());
            hullSplitIter.Last().AddRange(hullRects);
#endif
            //split intersecting hulls into multiple parts to prevent overlaps
            for (int i = 0; i < hullRects.Count; i++)
            {
                if (hullRects[i].Width <= 0 || hullRects[i].Height <= 0) continue;
                for (int j = 0; j < hullRects.Count; j++)
                {
                    if (i == j) continue;
                    if (hullRects[j].Width <= 0 || hullRects[j].Height <= 0) continue;
                    if (!hullRects[i].Intersects(hullRects[j])) continue;
                    
                    //hull i goes through hull j vertically
                    if (hullRects[i].X >= hullRects[j].X && hullRects[i].Right <= hullRects[j].Right &&
                        hullRects[i].Y <= hullRects[j].Y && hullRects[i].Bottom >= hullRects[j].Bottom)
                    {
                        Rectangle rectLeft = new Rectangle(hullRects[j].X, hullRects[j].Y, hullRects[i].X - hullRects[j].X, hullRects[j].Height);
                        Rectangle rectRight = new Rectangle(hullRects[i].Right, hullRects[j].Y, hullRects[j].Right - hullRects[i].Right, hullRects[j].Height);
                        hullRects[j] = rectLeft;
                        hullRects.Add(rectRight);
                    }                    
                    else if //hull i goes through hull j horizontally
                    (hullRects[i].Y >= hullRects[j].Y && hullRects[i].Bottom <= hullRects[j].Bottom &&
                    hullRects[i].X <= hullRects[j].X && hullRects[i].Right >= hullRects[j].Right)
                    {
                        Rectangle rectBottom = new Rectangle(hullRects[j].X, hullRects[j].Y, hullRects[j].Width, hullRects[i].Y - hullRects[j].Y);
                        Rectangle rectTop = new Rectangle(hullRects[j].X, hullRects[i].Bottom, hullRects[j].Width, hullRects[j].Bottom - hullRects[i].Bottom);
                        hullRects[j] = rectBottom;
                        hullRects.Add(rectTop);
                    }
                    //upper side of hull i is inside hull j
                    else if (hullRects[j].Contains(hullRects[i].Location) && hullRects[j].Contains(new Vector2(hullRects[i].Right, hullRects[i].Y)))
                    {
                        hullRects[i] = new Rectangle(hullRects[i].X, hullRects[j].Bottom, hullRects[i].Width, hullRects[i].Bottom - hullRects[j].Bottom);
                    }
                    //lower side of hull i is inside hull j 
                    else if (hullRects[j].Contains(new Vector2(hullRects[i].X, hullRects[i].Bottom)) && hullRects[j].Contains(new Vector2(hullRects[i].Right, hullRects[i].Bottom)))
                    {
                        hullRects[i] = new Rectangle(hullRects[i].X, hullRects[i].Y, hullRects[i].Width, hullRects[j].Y - hullRects[i].Y);
                    }
                    //left side of hull i is inside hull j
                    else if (hullRects[j].Contains(hullRects[i].Location) && hullRects[j].Contains(new Vector2(hullRects[i].X, hullRects[i].Bottom)))
                    {
                        hullRects[i] = new Rectangle(hullRects[j].X, hullRects[i].Y, hullRects[i].Right - hullRects[j].X, hullRects[i].Height);
                    }
                    //right side of hull i is inside hull j 
                    else if (hullRects[j].Contains(new Vector2(hullRects[i].Right, hullRects[i].Y)) && hullRects[j].Contains(new Vector2(hullRects[i].Right, hullRects[i].Bottom)))
                    {
                        hullRects[i] = new Rectangle(hullRects[i].X, hullRects[i].Y, hullRects[j].X - hullRects[i].X, hullRects[i].Height);
                    }
                    
#if DEBUG
                    hullSplitIter.Add(new List<Rectangle>());
                    hullSplitIter.Last().AddRange(hullRects);
#endif
                }
            }
            
            foreach (RuinShape room in allShapes)
            {
                if (room.RoomType == null) continue;
                //generate walls  --------------------------------------------------------------
                foreach (Line wall in room.Walls)
                {
                    var ruinEntityConfig = room.RoomType.GetRandomEntity(RuinEntityType.Wall, room.GetLineAlignment(wall));
                    if (ruinEntityConfig == null) continue;

                    wall.Radius = (wall.A.X == wall.B.X) ?
                        (ruinEntityConfig.Prefab as StructurePrefab).Size.X * 0.5f :
                        (ruinEntityConfig.Prefab as StructurePrefab).Size.Y * 0.5f;

                    Rectangle rect = new Rectangle(
                        (int)(wall.A.X - wall.Radius),
                        (int)(wall.B.Y + wall.Radius),
                        (int)((wall.B.X - wall.A.X) + wall.Radius * 2.0f),
                        (int)((wall.B.Y - wall.A.Y) + wall.Radius * 2.0f));

                    //cut a section off from both ends of a horizontal wall to get nicer looking corners 
                    if (wall.A.Y == wall.B.Y)
                    {
                        rect.Inflate(-32, 0);
                        if (rect.Width < Submarine.GridSize.X) continue;
                    }

                    var structure = new Structure(rect, ruinEntityConfig.Prefab as StructurePrefab, null)
                    {
                        ShouldBeSaved = false
                    };
                    structure.SetCollisionCategory(Physics.CollisionLevel);
                    CreateChildEntities(ruinEntityConfig, structure, room);
                    ruinEntities.Add(new RuinEntity(ruinEntityConfig, structure, room));
                }

                //generate backgrounds --------------------------------------------------------------
                var backgroundConfig = room.RoomType.GetRandomEntity(RuinEntityType.Back, Alignment.Center);
                if (backgroundConfig != null)
                {
                    Rectangle backgroundRect = new Rectangle(room.Rect.X, room.Rect.Y + room.Rect.Height, room.Rect.Width, room.Rect.Height);
                    var backgroundStructure = new Structure(backgroundRect, (backgroundConfig.Prefab as StructurePrefab), null)
                    {
                        ShouldBeSaved = false
                    };
                    CreateChildEntities(backgroundConfig, backgroundStructure, room);
                    ruinEntities.Add(new RuinEntity(backgroundConfig, backgroundStructure, room));
                }

                var submarineBlocker = BodyFactory.CreateRectangle(GameMain.World,
                    ConvertUnits.ToSimUnits(room.Rect.Width),
                    ConvertUnits.ToSimUnits(room.Rect.Height),
                    1, ConvertUnits.ToSimUnits(room.Center));

                submarineBlocker.IsStatic = true;
                submarineBlocker.CollisionCategories = Physics.CollisionWall;
                submarineBlocker.CollidesWith = Physics.CollisionWall;
                
                //generate doors --------------------------------------------------------------
                if (room is Corridor corridor)
                {
                    var doorConfig = room.RoomType.GetRandomEntity(corridor.IsHorizontal ? RuinEntityType.Door : RuinEntityType.Hatch, Alignment.Center);
                    if (corridor != null && doorConfig != null)
                    {
                        //find all walls that are parallel to the corridor
                        var suitableWalls = corridor.IsHorizontal ?
                            corridor.Walls.FindAll(c => c.A.Y == c.B.Y) : corridor.Walls.FindAll(c => c.A.X == c.B.X);

                        if (suitableWalls.Any())
                        {
                            //choose a random wall to place the door next to
                            Vector2 doorPos = corridor.Center;
                            var wall = suitableWalls[Rand.Int(suitableWalls.Count, Rand.RandSync.Server)];
                            if (corridor.IsHorizontal)
                            {
                                doorPos.X = (wall.A.X + wall.B.X) / 2.0f;
                            }
                            else
                            {
                                doorPos.Y = (wall.A.Y + wall.B.Y) / 2.0f;
                            }
                            var doorItem = new Item(doorConfig.Prefab as ItemPrefab, doorPos, null)
                            {
                                ShouldBeSaved = false
                            };
                            CreateChildEntities(doorConfig, doorItem, corridor);
                            Door door = doorItem.GetComponent<Door>();
                            doors.Add(door);
                            door.IsOpen = Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) < 0.8f;
                            ruinEntities.Add(new RuinEntity(doorConfig, doorItem, room));

                            //split the hull the door is inside
                            for (int i = 0; i < hullRects.Count; i++)
                            {
                                if (!hullRects[i].Contains(doorPos)) continue;

                                if (corridor.IsHorizontal)
                                {
                                    Rectangle rectLeft = new Rectangle(hullRects[i].X, hullRects[i].Y, (int)doorPos.X - hullRects[i].X, hullRects[i].Height);
                                    Rectangle rectRight = new Rectangle((int)doorPos.X, hullRects[i].Y, hullRects[i].Right - (int)doorPos.X, hullRects[i].Height);
                                    hullRects[i] = rectLeft;
                                    hullRects.Add(rectRight);
                                }
                                else
                                {
                                    Rectangle rectBottom = new Rectangle(hullRects[i].X, hullRects[i].Y, hullRects[i].Width, (int)doorPos.Y - hullRects[i].Y);
                                    Rectangle rectTop = new Rectangle(hullRects[i].X, (int)doorPos.Y, hullRects[i].Width, hullRects[i].Bottom - (int)doorPos.Y);
                                    hullRects[i] = rectBottom;
                                    hullRects.Add(rectTop);
                                }
                                break;
                            }
                        }
                    }
                }

                //generate props --------------------------------------------------------------
                var props = room.RoomType.GetPropList();
                foreach (RuinEntityConfig prop in props)
                {
                    CreateEntity(prop, room, parent: null);
                }

                //create connections between all generated entities ---------------------------
                foreach (RuinEntity ruinEntity in ruinEntities)
                {
                    CreateConnections(ruinEntity);
                }
            }

            //create hulls ---------------------------
            foreach (Rectangle hullRect in hullRects)
            {
                if (hullRect.Width <= 0 || hullRect.Height <= 0) continue;
                var hull = new Hull(MapEntityPrefab.Find(null, "hull"),
                    new Rectangle(hullRect.X, hullRect.Y + hullRect.Height, hullRect.Width, hullRect.Height), submarine: null)
                {
                    ShouldBeSaved = false
                };
                RuinShape room = allShapes.Find(s => s.Rect.Contains(hullRect.Center));
                if (room?.RoomType != null)
                {
                    hull.WaterVolume = hull.Volume * Rand.Range(room.RoomType.MinWaterAmount, room.RoomType.MaxWaterAmount, Rand.RandSync.Server);
                }
                entityGrid.InsertEntity(hull);
            }

            //create gaps between hulls ---------------------------
            hullRects.Add(entranceRoom.Rect);
            for (int i = 0; i < hullRects.Count; i++)
            {
                if (hullRects[i].Width <= 0 || hullRects[i].Height <= 0) continue;
                for (int j = i + 1; j < hullRects.Count; j++)
                {
                    Rectangle? gapRect = null;
                    if (Math.Abs(hullRects[i].X - hullRects[j].Right) <= 1 && hullYIntersect(hullRects[i], hullRects[j]))
                    {
                        gapRect = new Rectangle(
                            hullRects[i].X - 3, Math.Max(hullRects[i].Y, hullRects[j].Y),
                            6, Math.Min(hullRects[i].Bottom, hullRects[j].Bottom) - Math.Max(hullRects[i].Y, hullRects[j].Y));
                    }
                    else if (Math.Abs(hullRects[i].Right - hullRects[j].X) <= 1 && hullYIntersect(hullRects[i], hullRects[j]))
                    {
                        gapRect = new Rectangle(
                            hullRects[i].Right - 3, Math.Max(hullRects[i].Y, hullRects[j].Y),
                            6, Math.Min(hullRects[i].Bottom, hullRects[j].Bottom) - Math.Max(hullRects[i].Y, hullRects[j].Y));
                    }
                    else if (Math.Abs(hullRects[i].Y - hullRects[j].Bottom) <= 1 && hullXIntersect(hullRects[i], hullRects[j]))
                    {
                        gapRect = new Rectangle(
                            Math.Max(hullRects[i].X, hullRects[j].X), hullRects[i].Y - 3,
                            Math.Min(hullRects[i].Right, hullRects[j].Right) - Math.Max(hullRects[i].X, hullRects[j].X), 6);
                    }
                    else if (Math.Abs(hullRects[i].Bottom - hullRects[j].Y) <= 1 && hullXIntersect(hullRects[i], hullRects[j]))
                    {
                        gapRect = new Rectangle(
                            Math.Max(hullRects[i].X, hullRects[j].X), hullRects[i].Bottom - 3,
                            Math.Min(hullRects[i].Right, hullRects[j].Right) - Math.Max(hullRects[i].X, hullRects[j].X), 6);
                    }

                    if (!gapRect.HasValue || gapRect.Value.Width <= 0 || gapRect.Value.Height <= 0) continue;

                    //doors create their own gaps, don't create an additional one if there's a door at this 
                    bool doorFound = false;
                    foreach (Door door in doors)
                    {
                        if (Math.Abs(door.Item.WorldPosition.X - gapRect.Value.Center.X) < 5 &&
                            Math.Abs(door.Item.WorldPosition.Y - gapRect.Value.Center.Y) < 5)
                        {
                            doorFound = true;
                            break;
                        }
                    }
                    if (doorFound) { continue; }
                    
                    new Gap(new Rectangle(gapRect.Value.X, gapRect.Value.Y + gapRect.Value.Height, gapRect.Value.Width, gapRect.Value.Height), 
                        isHorizontal: gapRect.Value.Height > gapRect.Value.Width, submarine: null)
                    {
                        ShouldBeSaved = false
                    };
                }
            }

            bool hullXIntersect(Rectangle rect1, Rectangle rect2)
            {
                return
                    (rect1.X >= rect2.X && rect1.X <= rect2.Right) ||
                    (rect2.X >= rect1.X && rect2.X <= rect1.Right);
            }
            bool hullYIntersect(Rectangle rect1, Rectangle rect2)
            {
                return
                    (rect1.Y >= rect2.Y && rect1.Y <= rect2.Bottom) ||
                    (rect2.Y >= rect1.Y && rect2.Y <= rect1.Bottom);
            }
        }

        private void CreateEntity(RuinEntityConfig entityConfig, RuinShape room, MapEntity parent)
        {
            if (room == null) return;

            Vector2 size = (entityConfig.Prefab is StructurePrefab) ? ((StructurePrefab)entityConfig.Prefab).Size : Vector2.Zero;
            
            Vector2 position = room.Rect.Center.ToVector2();
            if (entityConfig.Alignment.HasFlag(Alignment.Top))
            {
                position = new Vector2(Rand.Range(room.Rect.X + size.X, room.Rect.Right - size.X, Rand.RandSync.Server), room.Rect.Bottom - 64);
            }
            else if (entityConfig.Alignment.HasFlag(Alignment.Bottom))
            {
                position = new Vector2(Rand.Range(room.Rect.X + size.X, room.Rect.Right - size.X, Rand.RandSync.Server), room.Rect.Top + 64);
            }
            else if (entityConfig.Alignment.HasFlag(Alignment.Right))
            {
                position = new Vector2(room.Rect.Right - 64, Rand.Range(room.Rect.Y + size.X, room.Rect.Bottom - size.Y, Rand.RandSync.Server));
            }
            else if (entityConfig.Alignment.HasFlag(Alignment.Left))
            {
                position = new Vector2(room.Rect.X + 64, Rand.Range(room.Rect.Y + size.X, room.Rect.Bottom - size.Y, Rand.RandSync.Server));
            }

            MapEntity entity = null;
            if (entityConfig.Prefab is ItemPrefab)
            {
                entity = new Item((ItemPrefab)entityConfig.Prefab, position, null);
            }
            else if (entityConfig.Prefab is ItemAssemblyPrefab itemAssemblyPrefab)
            {
                var entities = itemAssemblyPrefab.CreateInstance(position, sub: null);
                foreach (MapEntity e in entities)
                {
                    if (e is Structure) e.ShouldBeSaved = false;
                }
                if (entityConfig.Expand)
                {
                    ExpandEntities(entities);
                }
            }
            else
            {
                entity = new Structure(new Rectangle(
                    (int)(position.X - size.X / 2.0f), (int)(position.Y + size.Y / 2.0f),
                    (int)size.X, (int)size.Y),
                    entityConfig.Prefab as StructurePrefab, null)
                {
                    ShouldBeSaved = false
                };
            }

            CreateChildEntities(entityConfig, entity, room);
            ruinEntities.Add(new RuinEntity(entityConfig, entity, room, parent));
        }

        private void CreateChildEntities(RuinEntityConfig parentEntityConfig, MapEntity parentEntity, RuinShape room)
        {
            foreach (RuinEntityConfig childEntity in parentEntityConfig.ChildEntities)
            {
                var childRoom = FindRoom(childEntity.PlacementRelativeToParent, room);
                if (childRoom != null)
                {
                    CreateEntity(childEntity, childRoom, parentEntity);
                }
            }
        }

        private void CreateConnections(RuinEntity entity)
        {
            foreach (RuinEntityConfig.EntityConnection connection in entity.Config.EntityConnections)
            {
                MapEntity targetEntity = null;
                if (connection.TargetEntityIdentifier == "parent")
                {
                    targetEntity = entity.Parent;
                }
                else if (!string.IsNullOrEmpty(connection.RoomName))
                {
                    RuinShape targetRoom = null;
                    if (Enum.TryParse(connection.RoomName, out RuinEntityConfig.RelativePlacement placement))
                    {
                        targetRoom = FindRoom(placement, entity.Room);
                    }
                    else
                    {
                        targetRoom = allShapes.Find(s => s.RoomType?.Name == connection.RoomName);
                    }

                    if (targetRoom == null)
                    {
                        DebugConsole.ThrowError("Error while generating ruins - could not find a room of the type \"" + connection.RoomName + "\".");
                    }
                    else
                    {
                        targetEntity = ruinEntities.GetRandom(e => 
                            e.Room == targetRoom && 
                            e.Entity.prefab?.Identifier == connection.TargetEntityIdentifier)?.Entity;
                    }
                }
                else
                {
                    targetEntity = ruinEntities.GetRandom(e => e.Entity.prefab?.Identifier == connection.TargetEntityIdentifier)?.Entity;
                }

                if (targetEntity == null) continue;

                if (connection.WireConnection != null)
                {
                    Item item = entity.Entity as Item;
                    if (item == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + entity.Entity.Name + "\" - the entity is not an item.");
                        return;
                    }
                    else if (item.Connections == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + entity.Entity.Name + "\" - the item does not have a connection panel component.");
                        return;
                    }

                    Item parentItem = entity.Parent as Item;
                    if (parentItem == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + parentItem.Name + "\" - the entity is not an item.");
                        return;
                    }
                    else if (parentItem.Connections == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + parentItem.Name + "\" - the item does not have a connection panel component.");
                        return;
                    }

                    //TODO: alien wire prefab w/ custom sprite?
                    var wirePrefab = MapEntityPrefab.Find(null, "blackwire") as ItemPrefab;

                    var conn1 = item.Connections.Find(c => c.Name == connection.WireConnection.First);
                    if (conn1 == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + item.Name +
                            "\" - the item does not have a connection named \"" + connection.WireConnection.First + "\".");
                        continue;
                    }
                    var conn2 = parentItem.Connections.Find(c => c.Name == connection.WireConnection.Second);
                    if (conn2 == null)
                    {
                        DebugConsole.ThrowError("Could not connect a wire to the ruin entity \"" + parentItem.Name +
                            "\" - the item does not have a connection named \"" + connection.WireConnection.Second + "\".");
                        continue;
                    }

                    var wire = new Item(wirePrefab, parentItem.WorldPosition, null).GetComponent<Items.Components.Wire>();
                    wire.Item.ShouldBeSaved = false;
                    conn1.TryAddLink(wire);
                    wire.Connect(conn1, true);
                    conn2.TryAddLink(wire);
                    wire.Connect(conn2, true);                    
                }
                else
                {
                    entity.Entity.linkedTo.Add(targetEntity);
                    targetEntity.linkedTo.Add(entity.Entity);
                }
            }
        }

        private void ExpandEntities(IEnumerable<MapEntity> entities)
        {
            float minX = entities.Min(e => e.WorldPosition.X);
            float maxX = entities.Max(e => e.WorldPosition.X);
            float minY = entities.Min(e => e.WorldPosition.Y);
            float maxY = entities.Max(e => e.WorldPosition.Y);
            Vector2 center = new Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f);

            foreach (MapEntity entity in entities)
            {
                Vector2 diff = entity.WorldPosition - center;
                if (diff.LengthSquared() < 0.0001f) continue;

                float rayLength = 1000.0f;
                Vector2 rayStart = entity.WorldPosition;
                Vector2 rayEnd = entity.WorldPosition + Vector2.Normalize(diff) * rayLength;
                Vector2? closestIntersection = null;
                float closestDist = rayLength * rayLength;
                foreach (Line line in walls)
                {
                    Vector2? intersection = MathUtils.GetLineIntersection(line.A, line.B, rayStart, rayEnd);
                    if (!intersection.HasValue) continue;

                    intersection = line.IsHorizontal ?
                        new Vector2(intersection.Value.X, intersection.Value.Y - Math.Sign(diff.Y) * line.Radius) :
                        new Vector2(intersection.Value.X - Math.Sign(diff.X) * line.Radius, intersection.Value.Y);

                    float dist = Vector2.DistanceSquared(rayStart, intersection.Value);
                    if (dist < closestDist)
                    {
                        closestIntersection = intersection.Value;
                        closestDist = dist;
                    }
                }

                if (closestIntersection.HasValue)
                {
                    Vector2 moveAmount = closestIntersection.Value - entity.WorldPosition;
                    Vector2 moveRatio = new Vector2(
                        Math.Abs(diff.X) / ((maxX - minX) * 0.5f),
                        Math.Abs(diff.Y) / ((maxY - minY) * 0.5f));
                    moveAmount = new Vector2(moveAmount.X * moveRatio.X, moveAmount.Y * moveRatio.Y);

                    if (entity is Item item)
                    {
                        var connectionPanel = item.GetComponent<ConnectionPanel>();
                        connectionPanel?.MoveConnectedWires(moveAmount);
                    }

                    entity.Move(moveAmount);
                }
            }
        }

        private RuinShape FindRoom(RuinEntityConfig.RelativePlacement placement, RuinShape relativeTo)
        {
            switch (placement)
            {
                case RuinEntityConfig.RelativePlacement.SameRoom:
                    return relativeTo;
                case RuinEntityConfig.RelativePlacement.NextRoom:
                    return FindNearestRoom(relativeTo, rooms, 1);
                case RuinEntityConfig.RelativePlacement.NextCorridor:
                    return FindNearestRoom(relativeTo, corridors, 1);
                case RuinEntityConfig.RelativePlacement.PreviousRoom:
                    return FindNearestRoom(relativeTo, rooms, -1);
                case RuinEntityConfig.RelativePlacement.PreviousCorridor:
                    return FindNearestRoom(relativeTo, corridors, -1);
                case RuinEntityConfig.RelativePlacement.FirstRoom:
                    return FindFirstRoom(rooms);
                case RuinEntityConfig.RelativePlacement.FirstCorridor:
                    return FindFirstRoom(corridors);
                case RuinEntityConfig.RelativePlacement.LastRoom:
                    return FindLastRoom(rooms);
                case RuinEntityConfig.RelativePlacement.LastCorridor:
                    return FindLastRoom(corridors);
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Find the nearest room relative to a specific room.
        /// </summary>
        /// <param name="relativeTo">The room to compare the distance with</param>
        /// <param name="roomList">List of rooms to check (use a list that only contains rooms/corridors if you want a specific types of rooms)</param>
        /// <param name="dir">Direction to check: 1 = find the next room, -1 = find the previous room</param>
        private RuinShape FindNearestRoom(RuinShape relativeTo, IEnumerable<RuinShape> roomList, int dir, Func<RuinShape, bool> predicate = null)
        {
            dir = Math.Sign(dir);
            RuinShape selectedRoom = null;
            foreach (RuinShape room in roomList)
            {
                if (room == relativeTo) continue;
                if (predicate != null && !predicate(room)) continue;
                int roomDir = Math.Sign(room.DistanceFromEntrance - relativeTo.DistanceFromEntrance);

                if (roomDir == 0 || roomDir == dir)
                {
                    if (selectedRoom == null)
                    {
                        selectedRoom = room;
                    }
                    else //room already selected, check if this one is closer
                    {
                        //closer than the previously selected room
                        if (Math.Abs(room.DistanceFromEntrance - relativeTo.DistanceFromEntrance) < 
                            Math.Abs(selectedRoom.DistanceFromEntrance - relativeTo.DistanceFromEntrance))
                        {
                            selectedRoom = room;
                        }
                        //same distance measured in room indices, select the room if the actual distance is smaller
                        else if (room.DistanceFromEntrance == selectedRoom.DistanceFromEntrance &&
                            Vector2.DistanceSquared(relativeTo.Center, room.Center) < Vector2.DistanceSquared(relativeTo.Center, selectedRoom.Center))
                        {
                            selectedRoom = room;
                        }
                    }
                }
            }
            return selectedRoom;
        }

        private RuinShape FindFirstRoom(IEnumerable<RuinShape> roomList, Func<RuinShape, bool> predicate = null)
        {
            if (!roomList.Any()) { return null; }
            RuinShape firstRoom = null;
            foreach (RuinShape room in roomList)
            {
                if (predicate != null && !predicate(room)) continue;
                if (firstRoom == null || room.DistanceFromEntrance < firstRoom.DistanceFromEntrance)
                {
                    firstRoom = room;
                }
            }
            return firstRoom;
        }

        private RuinShape FindLastRoom(IEnumerable<RuinShape> roomList, Func<RuinShape, bool> predicate = null)
        {
            if (!roomList.Any()) { return null; }
            RuinShape lastRoom = null;
            foreach (RuinShape room in roomList)
            {
                if (predicate != null && !predicate(room)) continue;
                if (lastRoom == null || room.DistanceFromEntrance > lastRoom.DistanceFromEntrance)
                {
                    lastRoom = room;
                }
            }
            return lastRoom;
        }

    }
}
