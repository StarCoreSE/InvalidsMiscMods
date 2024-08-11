using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRageMath;
using VRageRender;
using System;
using System.Collections.Generic;
using System.Text;
using Draygo.API;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class CockpitHudDisplay : MySessionComponentBase
{
    private IMyCockpit _playerCockpit;
    private bool _isPlayerInCockpit = false;
    private PlanarMap _planarMap;
    private Legend _legend;
    private IEnumerator<float> _spriteDrawStateMachine = null;
    private bool _drawInProgress = false;
    private int _frameCounter = 0;

    private static HudAPIv2 _hudAPI;
    private HudAPIv2.HUDMessage _hudMessage;
    private StringBuilder _hudText;

    public override void LoadData()
    {
        InitializeHudAPI();
    }

    protected override void UnloadData()
    {
        PurgeHUDMessage();
    }

    private void InitializeHudAPI()
    {
        if (_hudAPI == null && !MyAPIGateway.Utilities.IsDedicated)
        {
            _hudAPI = new HudAPIv2();
        }
    }

    public override void UpdateAfterSimulation()
    {
        UpdatePlayerCockpitStatus();

        if (!_isPlayerInCockpit || _playerCockpit == null || !IsAPIAlive())
            return;

        try
        {
            // Rate limiting - update every 10th frame
            _frameCounter++;
            if (_frameCounter % 10 == 0)
            {
                if (!_drawInProgress)
                {
                    _spriteDrawStateMachine = SpriteDrawStateMachine();
                    _drawInProgress = true;
                }

                UpdateSpriteDrawStateMachine();
            }
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLineAndConsole($"{e.Message}\n{e.StackTrace}");
            MyAPIGateway.Utilities.ShowNotification($"[ ERROR: {GetType().FullName}: {e.Message} ]", 10000, MyFontEnum.Red);
        }
    }

    private void UpdatePlayerCockpitStatus()
    {
        var controlledEntity = MyAPIGateway.Session?.ControlledObject as IMyCockpit;
        if (controlledEntity != null && controlledEntity.ControllerInfo?.Controller == MyAPIGateway.Session.Player.Controller)
        {
            if (_playerCockpit != controlledEntity)
            {
                _playerCockpit = controlledEntity;
                _isPlayerInCockpit = true;
                InitializeMapAndLegend();
            }
        }
        else
        {
            _isPlayerInCockpit = false;
            _playerCockpit = null;
        }
    }

    private void InitializeMapAndLegend()
    {
        _planarMap = new PlanarMap(_playerCockpit.CubeGrid);
        _legend = new Legend(0.5f); // Example font size
        _legend.AddLegendItem("MISSING", "Damage", Color.Red);
        _legend.AddLegendItem("WEAPONS", "Weapons", Color.Orange);
        _legend.AddLegendItem("POWER", "Power", Color.Green);
        _legend.AddLegendItem("GYRO", "Gyros", Color.Yellow);
        _legend.AddLegendItem("THRUST", "Thrust", Color.Blue);

        StoreGridBlocks();
    }

    private void UpdateHUDMessage(Vector2D position, double scale)
    {
        if (_hudMessage == null)
        {
            _hudMessage = new HudAPIv2.HUDMessage(
                Message: _hudText,
                Origin: position,
                Offset: null,
                TimeToLive: -1,
                Scale: scale,
                HideHud: false,
                Shadowing: true,
                ShadowColor: Color.Black,
                Blend: BlendTypeEnum.PostPP,
                Font: "white"
            );
        }
        else
        {
            _hudMessage.Message.Clear().Append(_hudText);
            _hudMessage.Visible = true;
        }
    }

    private void StoreGridBlocks()
    {
        List<IMySlimBlock> blocks = new List<IMySlimBlock>();
        _playerCockpit.CubeGrid.GetBlocks(blocks);

        foreach (var block in blocks)
        {
            var blockPosition = block.Position;
            var blockInfo = new BlockInfo(ref blockPosition, _playerCockpit.CubeGrid);
            _planarMap.StoreBlockInfo(blockInfo);
        }
        _planarMap.CreateQuadTrees();
    }

    private IEnumerator<float> SpriteDrawStateMachine()
    {
        Vector2D hudPosition = new Vector2D(-0.1, -0.96); // Example position
        float scale = 1.0f;
        int totalNodes = _planarMap.QuadTreeXNormal.FinishedNodes.Count;
        int nodesProcessed = 0;

        if (_hudText == null)
        {
            _hudText = new StringBuilder(500);
        }
        _hudText.Clear();

        foreach (var leaf in _planarMap.QuadTreeXNormal.FinishedNodes)
        {
            // Generate the text or sprite information
            _hudText.AppendLine($"Block at {leaf.Min} - Status: {_planarMap.GetColor(leaf.Value)}");
            nodesProcessed++;

            // Call AddSpriteFromQuadTreeLeaf to render this leaf's information on the HUD
            _planarMap.QuadTreeXNormal.AddSpriteFromQuadTreeLeaf(_hudAPI, leaf, hudPosition, scale);

            // Yield after processing a portion of the nodes to avoid long frame times
            if (nodesProcessed % 100 == 0)
            {
                UpdateHUDMessage(hudPosition, scale);
                yield return nodesProcessed / (float)totalNodes;
            }
        }

        // Draw the legend on the HUD after all blocks are processed
        _hudText.AppendLine("Legend:");
        _legend.GenerateSprites(null, Vector2.Zero, scale); // Example call; you can adjust this as needed

        UpdateHUDMessage(hudPosition, scale);

        yield return 1.0f;
    }

    private void UpdateSpriteDrawStateMachine()
    {
        if (_spriteDrawStateMachine == null)
            return;

        bool moreInstructions = _spriteDrawStateMachine.MoveNext();
        if (!moreInstructions)
        {
            _spriteDrawStateMachine.Dispose();
            _spriteDrawStateMachine = null;
            _drawInProgress = false;
        }
    }

    private void PurgeHUDMessage()
    {
        if (_hudMessage != null)
        {
            _hudMessage.Visible = false;
            _hudMessage.DeleteMessage();
            _hudMessage = null;
        }
    }

    private static bool IsAPIAlive() => _hudAPI != null && _hudAPI.Heartbeat;
}

// Supporting Classes
public class PlanarMap
{
    public List<QuadTreeLeaf> FinishedNodes = new List<QuadTreeLeaf>();
    public QuadTree QuadTreeXNormal = new QuadTree();

    private IMyCubeGrid _grid;
    private int[,] _densityXNormal;
    private int _maxDensityX;

    public PlanarMap(IMyCubeGrid grid)
    {
        _grid = grid;
        InitializeDensityMap();
    }

    private void InitializeDensityMap()
    {
        // Calculate the dimensions for the density map based on the grid
        Vector3I gridMin = _grid.Min;
        Vector3I gridMax = _grid.Max;

        Vector3I gridDimensions = gridMax - gridMin + Vector3I.One;

        _densityXNormal = new int[gridDimensions.Y, gridDimensions.Z];
        _maxDensityX = 0;
    }

    public void StoreBlockInfo(BlockInfo info)
    {
        var diff = info.GridPosition - _grid.Min;

        _densityXNormal[diff.Y, diff.Z] += 1;

        _maxDensityX = Math.Max(_maxDensityX, _densityXNormal[diff.Y, diff.Z]);

        // Add block info to the appropriate quadtree or other storage as needed
    }

    public void CreateQuadTrees()
    {
        // Create and initialize the quadtree based on the collected data
        QuadTreeXNormal.Initialize(_densityXNormal, _maxDensityX);
    }

    public Color GetColor(int value)
    {
        // Map the value to a color. This example uses a linear gradient between red and green.
        float lerpScale = (float)value / _maxDensityX;
        return Color.Lerp(Color.Red, Color.Green, lerpScale);
    }
}

public class QuadTree
{
    public List<QuadTreeLeaf> FinishedNodes = new List<QuadTreeLeaf>();

    public void Initialize(int[,] densityMap, int maxDensity)
    {
        int height = densityMap.GetLength(0);
        int width = densityMap.GetLength(1);

        // Loop through each cell in the density map
        for (int y = 0; y < height; y++)
        {
            for (int z = 0; z < width; z++)
            {
                int value = densityMap[y, z];
                if (value > 0)
                {
                    // Create a new QuadTreeLeaf and add it to the FinishedNodes
                    Vector2I min = new Vector2I(y, z);
                    Vector2I max = new Vector2I(y, z); // This can be expanded if you want larger blocks

                    FinishedNodes.Add(new QuadTreeLeaf(min, max, value));
                }
            }
        }
    }

    public void AddSpriteFromQuadTreeLeaf(HudAPIv2 hudAPI, QuadTreeLeaf leaf, Vector2D position, float scale)
    {
        // Calculate color based on the leaf value (this is an example)
        Color color = Color.Lerp(Color.Red, Color.Green, leaf.Value / 100f);

        // Create and display the sprite based on the leaf's position and value
        // For example, you could use the leaf's Min and Max to determine the position and size
        StringBuilder spriteText = new StringBuilder();
        spriteText.Append($"Block at ({leaf.Min.X}, {leaf.Min.Y}) - Value: {leaf.Value}");

        // Display the sprite (adjust position and other parameters as needed)
        var spriteMessage = new HudAPIv2.HUDMessage(
            Message: spriteText,
            Origin: position,
            Offset: null,
            TimeToLive: -1,
            Scale: scale,
            HideHud: false,
            Shadowing: true,
            ShadowColor: Color.Black,
            Blend: BlendTypeEnum.PostPP,
            Font: "white"
        );
    }
}

public class QuadTreeLeaf
{
    public Vector2I Min;
    public Vector2I Max;
    public int Value;

    public QuadTreeLeaf(Vector2I min, Vector2I max, int value)
    {
        Min = min;
        Max = max;
        Value = value;
    }
}

public class Legend
{
    private Dictionary<string, LegendItem> _legendItems = new Dictionary<string, LegendItem>();
    private float _legendFontSize;

    public Legend(float fontSize)
    {
        _legendFontSize = fontSize;
    }

    public void AddLegendItem(string key, string name, Color color)
    {
        _legendItems[key] = new LegendItem(name, color);
    }

    public void GenerateSprites(object surf, Vector2 topLeftPos, float scale)
    {
        // Implement your legend drawing logic here
        // This should create and render the legend on the HUD
    }
}

public class LegendItem
{
    public string Name;
    public Color Color;

    public LegendItem(string name, Color color)
    {
        Name = name;
        Color = color;
    }
}

public class BlockInfo
{
    public Vector3I GridPosition;

    public BlockInfo(ref Vector3I gridPosition, IMyCubeGrid grid)
    {
        GridPosition = gridPosition;
        // Implement your block info storage logic
    }
}
