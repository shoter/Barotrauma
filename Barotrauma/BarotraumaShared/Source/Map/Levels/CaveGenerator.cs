﻿using FarseerPhysics;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Voronoi2;

namespace Barotrauma
{
    static partial class CaveGenerator
    {
        /*public static List<VoronoiCell> CarveCave(List<VoronoiCell> cells, Vector2 startPoint, out List<VoronoiCell> newCells)
        {
            Voronoi voronoi = new Voronoi(1.0);

            List<Vector2> sites = new List<Vector2>();

            float siteInterval = 400.0f;
            float siteVariance = siteInterval * 0.4f;

            Vector4 edges = new Vector4(
                cells.Min(x => x.edges.Min(e => e.Point1.X)),
                cells.Min(x => x.edges.Min(e => e.Point1.Y)),
                cells.Max(x => x.edges.Max(e => e.Point1.X)),
                cells.Max(x => x.edges.Max(e => e.Point1.Y)));

            edges.X -= siteInterval * 2;
            edges.Y -= siteInterval * 2;
            edges.Z += siteInterval * 2;
            edges.W += siteInterval * 2;

            Rectangle borders = new Rectangle((int)edges.X, (int)edges.Y, (int)(edges.Z - edges.X), (int)(edges.W - edges.Y));

            for (float x = edges.X + siteInterval; x < edges.Z - siteInterval; x += siteInterval)
            {
                for (float y = edges.Y + siteInterval; y < edges.W - siteInterval; y += siteInterval)
                {
                    if (Rand.Int(5, Rand.RandSync.Server) == 0) continue; //skip some positions to make the cells more irregular

                    sites.Add(new Vector2(x, y) + Rand.Vector(siteVariance, Rand.RandSync.Server));
                }
            }

            List<GraphEdge> graphEdges = voronoi.MakeVoronoiGraph(sites, edges.X, edges.Y, edges.Z, edges.W);

            List<VoronoiCell>[,] cellGrid;
            newCells = GraphEdgesToCells(graphEdges, borders, 1000, out cellGrid);

            foreach (VoronoiCell cell in newCells)
            {
                //if the cell is at the edge of the graph, remove it
                if (cell.edges.Any(e => 
                    e.Point1.X == edges.X || e.Point1.X == edges.Z ||
                    e.Point1.Y == edges.Z || e.Point1.Y == edges.W))
                {
                    cell.CellType = CellType.Removed;
                    continue;
                }
                
                //remove cells that aren't inside any of the original "base cells"
                if (cells.Any(c => c.IsPointInside(cell.Center))) continue;
                foreach (GraphEdge edge in cell.edges)
                {
                    //mark all the cells adjacent to the removed cell as edges of the cave
                    var adjacent = edge.AdjacentCell(cell);
                    if (adjacent != null && adjacent.CellType != CellType.Removed) adjacent.CellType = CellType.Edge;
                }

                cell.CellType = CellType.Removed;
            }

            newCells.RemoveAll(newCell => newCell.CellType == CellType.Removed);

            //start carving from the edge cell closest to the startPoint
            VoronoiCell startCell = null;
            float closestDist = 0.0f;
            foreach (VoronoiCell cell in newCells)
            {
                if (cell.CellType != CellType.Edge) continue;

                float dist = Vector2.Distance(startPoint, cell.Center);
                if (dist < closestDist || startCell == null)
                {
                    startCell = cell;
                    closestDist = dist;
                }
            }

            startCell.CellType = CellType.Path;

            List<VoronoiCell> path = new List<VoronoiCell>() {startCell};
            VoronoiCell pathCell = startCell;
            for (int i = 0; i < newCells.Count / 2; i++)
            {
                var allowedNextCells = new List<VoronoiCell>();
                foreach (GraphEdge edge in pathCell.edges)
                {
                    var adjacent = edge.AdjacentCell(pathCell);
                    if (adjacent == null ||
                        adjacent.CellType == CellType.Removed ||
                        adjacent.CellType == CellType.Edge) continue;

                    allowedNextCells.Add(adjacent);
                }

                if (allowedNextCells.Count == 0)
                {
                    if (i>5) break;
                    
                    foreach (GraphEdge edge in pathCell.edges)
                    {
                        var adjacent = edge.AdjacentCell(pathCell);
                        if (adjacent == null ||
                            adjacent.CellType == CellType.Removed) continue;

                        allowedNextCells.Add(adjacent);
                    }

                    if (allowedNextCells.Count == 0) break;
                }
                
                //randomly pick one of the adjacent cells as the next cell
                pathCell = allowedNextCells[Rand.Int(allowedNextCells.Count, Rand.RandSync.Server)];

                //randomly take steps further away from the startpoint to make the cave expand further
                if (Rand.Int(4, Rand.RandSync.Server) == 0)
                {
                    float furthestDist = 0.0f;
                    foreach (VoronoiCell nextCell in allowedNextCells)
                    {
                        float dist = Vector2.Distance(startCell.Center, nextCell.Center);
                        if (dist > furthestDist || furthestDist == 0.0f)
                        {
                            furthestDist = dist;
                            pathCell = nextCell;
                        }
                    }
                }

                pathCell.CellType = CellType.Path;
                path.Add(pathCell);
            }

            //make sure the tunnel is always wider than minPathWidth
            float minPathWidth = 100.0f;
            for (int i = 0; i < path.Count; i++)
            {
                var cell = path[i];
                foreach (GraphEdge edge in cell.edges)
                {
                    if (edge.Point1 == edge.Point2) continue;
                    if (Vector2.Distance(edge.Point1, edge.Point2) > minPathWidth) continue;
                    
                    GraphEdge adjacentEdge = cell.edges.Find(e => e != edge && (e.Point1 == edge.Point1 || e.Point2 == edge.Point1));

                    var adjacentCell = adjacentEdge.AdjacentCell(cell);
                    if (i>0 && (adjacentCell.CellType == CellType.Path || adjacentCell.CellType == CellType.Edge)) continue;

                    adjacentCell.CellType = CellType.Path;
                    path.Add(adjacentCell);
                }
            }

            return path;
        }*/

        public static List<VoronoiCell> GraphEdgesToCells(List<GraphEdge> graphEdges, Rectangle borders, float gridCellSize, out List<VoronoiCell>[,] cellGrid)
        {
            List<VoronoiCell> cells = new List<VoronoiCell>();

            cellGrid = new List<VoronoiCell>[(int)Math.Ceiling(borders.Width / gridCellSize), (int)Math.Ceiling(borders.Height / gridCellSize)];
            for (int x = 0; x < borders.Width / gridCellSize; x++)
            {
                for (int y = 0; y < borders.Height / gridCellSize; y++)
                {
                    cellGrid[x, y] = new List<VoronoiCell>();
                }
            }

            foreach (GraphEdge ge in graphEdges)
            {
                if (Vector2.DistanceSquared(ge.Point1, ge.Point2) < 0.001f) continue;

                for (int i = 0; i < 2; i++)
                {
                    Site site = (i == 0) ? ge.Site1 : ge.Site2;

                    int x = (int)(Math.Floor((site.coord.x-borders.X) / gridCellSize));
                    int y = (int)(Math.Floor((site.coord.y-borders.Y) / gridCellSize));

                    x = MathHelper.Clamp(x, 0, cellGrid.GetLength(0)-1);
                    y = MathHelper.Clamp(y, 0, cellGrid.GetLength(1)-1);
                        
                    VoronoiCell cell = cellGrid[x,y].Find(c => c.site == site);

                    if (cell == null)
                    {
                        cell = new VoronoiCell(site);
                        cellGrid[x, y].Add(cell);
                        cells.Add(cell);
                    }

                    if (ge.Cell1 == null)
                    {
                        ge.Cell1 = cell;
                    }
                    else
                    {
                        ge.Cell2 = cell;
                    }
                    cell.edges.Add(ge);
                }
            }

            return cells;
        }


        private static Vector2 GetEdgeNormal(GraphEdge edge, VoronoiCell cell = null)
        {
            if (cell == null) cell = edge.AdjacentCell(null);
            if (cell == null) return Vector2.UnitX;

            CompareCCW compare = new CompareCCW(cell.Center);
            if (compare.Compare(edge.Point1, edge.Point2) == -1)
            {
                var temp = edge.Point1;
                edge.Point1 = edge.Point2;
                edge.Point2 = temp;
            }

            Vector2 normal = Vector2.Zero;

            normal = Vector2.Normalize(edge.Point2 - edge.Point1);
            Vector2 diffToCell = Vector2.Normalize(cell.Center - edge.Point2);

            normal = new Vector2(-normal.Y, normal.X);
            if (Vector2.Dot(normal, diffToCell) < 0)
            {
                normal = -normal;
            }

            return normal;
        }

        public static List<VoronoiCell> GeneratePath(
            List<Vector2> pathNodes, List<VoronoiCell> cells, List<VoronoiCell>[,] cellGrid,
            int gridCellSize, Rectangle limits, float wanderAmount = 0.3f, bool mirror = false, Vector2? gridOffset = null)
        {
            var targetCells = new List<VoronoiCell>();
            for (int i = 0; i < pathNodes.Count; i++)
            {
                //a search depth of 2 is large enough to find a cell in almost all maps, but in case it fails, we increase the depth
                int searchDepth = 2;
                while (searchDepth < 5)
                {
                    int cellIndex = FindCellIndex(pathNodes[i], cells, cellGrid, gridCellSize, searchDepth, gridOffset);
                    if (cellIndex > -1)
                    {
                        targetCells.Add(cells[cellIndex]);
                        break;
                    }

                    searchDepth++;
                }

            }

            return GeneratePath(targetCells, cells, cellGrid, gridCellSize, limits, wanderAmount, mirror);
        }


        public static List<VoronoiCell> GeneratePath(
            List<VoronoiCell> targetCells, List<VoronoiCell> cells, List<VoronoiCell>[,] cellGrid, 
            int gridCellSize, Rectangle limits, float wanderAmount = 0.3f, bool mirror = false)
        {
            Stopwatch sw2 = new Stopwatch();
            sw2.Start();

            //how heavily the path "steers" towards the endpoint
            //lower values will cause the path to "wander" more, higher will make it head straight to the end
            wanderAmount = MathHelper.Clamp(wanderAmount, 0.0f, 1.0f);

            List<GraphEdge> allowedEdges = new List<GraphEdge>();
            List<VoronoiCell> pathCells = new List<VoronoiCell>();
            
            VoronoiCell currentCell = targetCells[0];
            currentCell.CellType = CellType.Path;
            pathCells.Add(currentCell);

            int currentTargetIndex = 1;

            int iterationsLeft = cells.Count;

            do
            {
                int edgeIndex = 0;

                allowedEdges.Clear();
                foreach (GraphEdge edge in currentCell.edges)
                {
                    if (!limits.Contains(edge.AdjacentCell(currentCell).Center)) continue;

                    allowedEdges.Add(edge);
                }

                //steer towards target
                if (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) > wanderAmount || allowedEdges.Count == 0)
                {
                    for (int i = 0; i < currentCell.edges.Count; i++)
                    {
                        if (!MathUtils.LinesIntersect(currentCell.Center, targetCells[currentTargetIndex].Center,
                            currentCell.edges[i].Point1, currentCell.edges[i].Point2)) continue;
                        edgeIndex = i;
                        break;
                    }
                }
                //choose random edge (ignoring ones where the adjacent cell is outside limits)
                else
                {
                    edgeIndex = Rand.Int(allowedEdges.Count, Rand.RandSync.Server);
                    if (mirror && edgeIndex > 0) edgeIndex = allowedEdges.Count - edgeIndex;
                    edgeIndex = currentCell.edges.IndexOf(allowedEdges[edgeIndex]);
                }

                currentCell = currentCell.edges[edgeIndex].AdjacentCell(currentCell);
                currentCell.CellType = CellType.Path;
                pathCells.Add(currentCell);

                iterationsLeft--;

                if (currentCell == targetCells[currentTargetIndex])
                {
                    currentTargetIndex += 1;
                    if (currentTargetIndex >= targetCells.Count) break;
                }

            } while (currentCell != targetCells[targetCells.Count - 1] && iterationsLeft > 0);


            Debug.WriteLine("gettooclose: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            return pathCells;
        }

        /// <summary>
        /// Makes the cell rounder by subdividing the edges and offsetting them at the middle
        /// </summary>
        /// <param name="minEdgeLength">How small the individual subdivided edges can be (smaller values produce rounder shapes, but require more geometry)</param>
        public static void RoundCell(VoronoiCell cell, float minEdgeLength = 500.0f, float roundingAmount = 0.5f, float irregularity = 0.1f)
        {
            List<GraphEdge> tempEdges = new List<GraphEdge>();
            foreach (GraphEdge edge in cell.edges)
            {
                if (!edge.IsSolid)
                {
                    tempEdges.Add(edge);
                    continue;
                }

                List<Vector2> edgePoints = new List<Vector2>();
                Vector2 edgeNormal = GetEdgeNormal(edge, cell);
                float edgeLength = Vector2.Distance(edge.Point1, edge.Point2);
                int pointCount = (int)Math.Max(Math.Ceiling(edgeLength / minEdgeLength), 1);
                Vector2 edgeDir = (edge.Point2 - edge.Point1);
                for (int i = 0; i <= pointCount; i++)
                {
                    if (i == 0)
                    {
                        edgePoints.Add(edge.Point1);
                    }
                    else if (i == pointCount)
                    {
                        edgePoints.Add(edge.Point2);
                    }
                    else
                    {
                        float centerF = 0.5f - Math.Abs(0.5f - (i / (float)pointCount));
                        float randomVariance = Rand.Range(0, irregularity, Rand.RandSync.Server);
                        edgePoints.Add(
                            edge.Point1 +
                            edgeDir * (i / (float)pointCount) -
                            edgeNormal * edgeLength * (roundingAmount + randomVariance) * centerF);
                    }
                }

                for (int i = 0; i < pointCount; i++)
                {
                    tempEdges.Add(new GraphEdge(edgePoints[i], edgePoints[i + 1])
                    {
                        Cell1 = edge.Cell1,
                        Cell2 = edge.Cell2,
                        IsSolid = edge.IsSolid,
                        Site1 = edge.Site1,
                        Site2 = edge.Site2,
                        OutsideLevel = edge.OutsideLevel
                    });
                }
            }

            cell.edges = tempEdges;
        }

        public static Body GeneratePolygons(List<VoronoiCell> cells, Level level, out List<Vector2[]> renderTriangles)
        {
            renderTriangles = new List<Vector2[]>();

            List<Vector2> tempVertices = new List<Vector2>();
            List<Vector2> bodyPoints = new List<Vector2>();

            Body cellBody = new Body(GameMain.World)
            {
                SleepingAllowed = false,
                BodyType = BodyType.Static,
                CollisionCategories = Physics.CollisionLevel
            };

            for (int n = cells.Count - 1; n >= 0; n-- )
            {
                VoronoiCell cell = cells[n];
                
                bodyPoints.Clear();
                tempVertices.Clear();
                foreach (GraphEdge ge in cell.edges)
                {
                    if (Vector2.DistanceSquared(ge.Point1, ge.Point2) < 0.01f) continue;
                    if (!tempVertices.Any(v => Vector2.DistanceSquared(ge.Point1, v) < 1.0f))
                    {
                        tempVertices.Add(ge.Point1);
                        bodyPoints.Add(ge.Point1);
                    }
                    if (!tempVertices.Any(v => Vector2.DistanceSquared(ge.Point2, v) < 1.0f))
                    {
                        tempVertices.Add(ge.Point2);
                        bodyPoints.Add(ge.Point2);
                    }
                }

                if (tempVertices.Count < 3 || bodyPoints.Count < 2)
                {
                    cells.RemoveAt(n);
                    continue;
                }

                renderTriangles.AddRange(MathUtils.TriangulateConvexHull(tempVertices, cell.Center));
                
                if (bodyPoints.Count < 2) continue;

                if (bodyPoints.Count < 3)
                {
                    foreach (Vector2 vertex in tempVertices)
                    {
                        if (bodyPoints.Contains(vertex)) continue;
                        bodyPoints.Add(vertex);
                        break;
                    }
                }

                for (int i = 0; i < bodyPoints.Count; i++)
                {
                    cell.bodyVertices.Add(bodyPoints[i]);
                    bodyPoints[i] = ConvertUnits.ToSimUnits(bodyPoints[i]);
                }
                
                if (cell.CellType == CellType.Empty) continue;

                cellBody.UserData = cell;
                var triangles = MathUtils.TriangulateConvexHull(bodyPoints, ConvertUnits.ToSimUnits(cell.Center));
                
                for (int i = 0; i < triangles.Count; i++)
                {
                    //don't create a triangle if the area of the triangle is too small
                    //(apparently Farseer doesn't like polygons with a very small area, see Shape.ComputeProperties)
                    Vector2 a = triangles[i][0];
                    Vector2 b = triangles[i][1];
                    Vector2 c = triangles[i][2];
                    float area = Math.Abs(a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y)) / 2.0f;
                    if (area < 1.0f) continue;

                    Vertices bodyVertices = new Vertices(triangles[i]);
                    var newFixture = FixtureFactory.AttachPolygon(bodyVertices, 5.0f, cellBody);
                    newFixture.UserData = cell;

                    if (newFixture.Shape.MassData.Area < FarseerPhysics.Settings.Epsilon)
                    {
                        DebugConsole.ThrowError("Invalid triangle created by CaveGenerator (" + triangles[i][0] + ", " + triangles[i][1] + ", " + triangles[i][2] + ")");
                        GameAnalyticsManager.AddErrorEventOnce(
                            "CaveGenerator.GeneratePolygons:InvalidTriangle",
                            GameAnalyticsSDK.Net.EGAErrorSeverity.Warning,
                            "Invalid triangle created by CaveGenerator (" + triangles[i][0] + ", " + triangles[i][1] + ", " + triangles[i][2] + "). Seed: " + level.Seed);
                    }
                }
                
                cell.body = cellBody;
            }

            return cellBody;
        }
        
        public static List<Vector2> CreateRandomChunk(float radius, int vertexCount, float radiusVariance)
        {
            Debug.Assert(radiusVariance < radius);
            Debug.Assert(vertexCount >= 3);

            List<Vector2> verts = new List<Vector2>();
            float angleStep = MathHelper.TwoPi / vertexCount;
            float angle = 0.0f;
            for (int i = 0; i < vertexCount; i++)
            {
                verts.Add(new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) *
                    (radius + Rand.Range(-radiusVariance, radiusVariance, Rand.RandSync.Server)));
                angle += angleStep;
            }
            return verts;
        }

        /// <summary>
        /// find the index of the cell which the point is inside
        /// (actually finds the cell whose center is closest, but it's always the correct cell assuming the point is inside the borders of the diagram)
        /// </summary>
        public static int FindCellIndex(Vector2 position,List<VoronoiCell> cells, List<VoronoiCell>[,] cellGrid, int gridCellSize, int searchDepth = 1, Vector2? offset = null)
        {
            float closestDist = 0.0f;
            VoronoiCell closestCell = null;

            Vector2 gridOffset = offset == null ? Vector2.Zero : (Vector2)offset;
            position -= gridOffset;

            int gridPosX = (int)Math.Floor(position.X / gridCellSize);
            int gridPosY = (int)Math.Floor(position.Y / gridCellSize);

            for (int x = Math.Max(gridPosX - searchDepth, 0); x <= Math.Min(gridPosX + searchDepth, cellGrid.GetLength(0) - 1); x++)
            {
                for (int y = Math.Max(gridPosY - searchDepth, 0); y <= Math.Min(gridPosY + searchDepth, cellGrid.GetLength(1) - 1); y++)
                {
                    for (int i = 0; i < cellGrid[x, y].Count; i++)
                    {
                        float dist = Vector2.Distance(cellGrid[x, y][i].Center, position);
                        if (closestDist != 0.0f && dist > closestDist) continue;

                        closestDist = dist;
                        closestCell = cellGrid[x, y][i];
                    }
                }
            }

            return cells.IndexOf(closestCell);
        }


    }
}
