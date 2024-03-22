using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game;
using VRage.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Utils;
using VRageMath;

[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class SessionComp : MySessionComponentBase
{
    private bool isPredictionDisabled = true;
    private bool shouldDrawServerLine = false;
    private DateTime lastDrawn = DateTime.MinValue;
    private int timer = 0;
    private DateTime lastLineDrawn = DateTime.Now;
    private TimeSpan drawInterval = TimeSpan.FromMilliseconds(30);

    struct LineToDraw
    {
        public Vector3D Origin;
        public Vector3D Direction;
        public DateTime Timestamp;
        public Color Color;

        public LineToDraw(Vector3D origin, Vector3D direction, DateTime timestamp, Color color)
        {
            Origin = origin;
            Direction = direction;
            Timestamp = timestamp;
            Color = color;
        }
    }

    private List<LineToDraw> linesToDraw = new List<LineToDraw>();

    public override void LoadData()
    {
        if (!MyAPIGateway.Utilities.IsDedicated)
        {
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
        }
    }

    public override void UpdateAfterSimulation()
    {
        if (!MyAPIGateway.Utilities.IsDedicated)
        {
            // New code: Disable prediction for all grids in the world
            var allEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(allEntities, entity => entity is MyCubeGrid);

            foreach (var entity in allEntities)
            {
                var grid = entity as MyCubeGrid;
                if (grid != null)
                {
                    grid.ForceDisablePrediction = isPredictionDisabled;

                    // Debug notification for each grid
                    //MyAPIGateway.Utilities.ShowNotification($"Prediction Disabled for: {grid.DisplayName}", 1000/60, MyFontEnum.Blue);
                }
            }

            // Timer-based line drawing
            timer += 1; // Increment counter since UpdateAfterSimulation is called every millisecond

            if (timer >= 30) // Check if 30ms have passed
            {
                timer = 0; // Reset counter

                MyEntity controlledEntity = GetControlledGrid();
                if (controlledEntity != null && shouldDrawServerLine)
                {
                    var grid = controlledEntity as MyCubeGrid;
                    if (grid != null)
                    {
                        // Calculate the center of the grid
                        Vector3D gridCenter = grid.PositionComp.WorldAABB.Center;

                        // Use controlledEntity's forward direction
                        Vector3D direction = controlledEntity.WorldMatrix.Forward;

                        // Add the line to draw
                        linesToDraw.Add(new LineToDraw(gridCenter, direction, DateTime.Now, Color.Red));
                    }
                }
            }

            // Call DrawLines every update
            DrawLines();
        }
    }

    private MyEntity GetControlledGrid()
    {
        try
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null)
            {
                return null;
            }

            var controlledEntity = MyAPIGateway.Session.Player.Controller?.ControlledEntity?.Entity;
            return (MyEntity)controlledEntity;
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine($"Error in GetControlledGrid: {e}");
            return null;
        }
    }

    private void OnMessageEntered(string messageText, ref bool sendToOthers)
    {
        if (messageText.Equals("/toggleprediction"))
        {
            isPredictionDisabled = !isPredictionDisabled;
            MyAPIGateway.Utilities.ShowNotification($"ForceDisablePrediction: {isPredictionDisabled}", 2000, MyFontEnum.Red);
            sendToOthers = false;
        }
        else if (messageText.Equals("/toggleserverline"))
        {
            shouldDrawServerLine = !shouldDrawServerLine;
            sendToOthers = false;
        }
    }

    private void DrawLines()
    {
        float length = 100f;
        float thickness = 0.25f;

        DateTime now = DateTime.Now;

        linesToDraw.RemoveAll(line => (now - line.Timestamp).TotalSeconds >= 2);

        foreach (var line in linesToDraw)
        {
            if ((now - line.Timestamp).TotalSeconds < 2)
            {
                Vector4 colorVector = new Vector4(line.Color.R / 255.0f, line.Color.G / 255.0f, line.Color.B / 255.0f, line.Color.A / 255.0f);
                Vector3D endPoint = line.Origin + line.Direction * length;
                MySimpleObjectDraw.DrawLine(line.Origin, endPoint, MyStringId.GetOrCompute("Square"), ref colorVector, thickness);
            }
        }
    }

    protected override void UnloadData()
    {
        if (!MyAPIGateway.Utilities.IsDedicated)
        {
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
        }
    }
}
