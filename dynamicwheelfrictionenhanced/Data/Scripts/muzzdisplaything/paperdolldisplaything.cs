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
    private int _frameCounter = 0;

    private static HudAPIv2 _hudAPI;
    private List<HudAPIv2.HUDMessage> _hudMessages = new List<HudAPIv2.HUDMessage>();

    public override void LoadData()
    {
        InitializeHudAPI();
    }

    protected override void UnloadData()
    {
        ClearHUD();
    }

    private void InitializeHudAPI()
    {
        if (_hudAPI == null && !MyAPIGateway.Utilities.IsDedicated)
        {
            _hudAPI = new HudAPIv2(RegisterHudAPI);
        }
    }

    private void RegisterHudAPI()
    {
        MyLog.Default.WriteLine("HUD API Registered");
    }

    public override void UpdateAfterSimulation()
    {
        UpdatePlayerCockpitStatus();

        if (!_isPlayerInCockpit || _playerCockpit == null || !IsAPIAlive())
        {
            if (_frameCounter % 300 == 0) // Log every 300 frames
            {
                MyLog.Default.WriteLine($"Not drawing HUD. InCockpit: {_isPlayerInCockpit}, Cockpit: {_playerCockpit != null}, APIAlive: {IsAPIAlive()}");
            }
            return;
        }

        try
        {
            // Rate limiting - update every 30th frame
            _frameCounter++;
            if (_frameCounter % 30 == 0)
            {
                ClearHUD();
                DrawHUD();
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
            ClearHUD();
        }
    }

    private void InitializeMapAndLegend()
    {
        _planarMap = new PlanarMap(_playerCockpit.CubeGrid);
        _legend = new Legend(0.5f);
        _legend.AddLegendItem("MISSING", "Damage", Color.Red);
        _legend.AddLegendItem("WEAPONS", "Weapons", Color.Orange);
        _legend.AddLegendItem("POWER", "Power", Color.Green);
        _legend.AddLegendItem("GYRO", "Gyros", Color.Yellow);
        _legend.AddLegendItem("THRUST", "Thrust", Color.Blue);

        StoreGridBlocks();
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

    private void DrawHUD()
    {
        if (_hudAPI == null || !_hudAPI.Heartbeat)
        {
            MyLog.Default.WriteLine("HUD API is not ready");
            return;
        }

        Vector2D hudPosition = new Vector2D(-0.9, 0.5); // Adjust as needed
        double scale = 0.5; // Adjust as needed

        MyLog.Default.WriteLine($"Drawing HUD with {_planarMap.QuadTreeXNormal.FinishedNodes.Count} nodes");

        foreach (var leaf in _planarMap.QuadTreeXNormal.FinishedNodes)
        {
            AddSpriteFromQuadTreeLeaf(leaf, hudPosition, scale);
        }

        // Draw legend
        Vector2D legendPosition = new Vector2D(0.8, 0.5); // Adjust as needed
        _legend.GenerateSprites(_hudAPI, legendPosition, scale);

        MyLog.Default.WriteLine($"HUD drawn with {_hudMessages.Count} messages");
    }

    private void AddSpriteFromQuadTreeLeaf(QuadTreeLeaf leaf, Vector2D position, double scale)
    {
        Color color = _planarMap.GetColor(leaf.Value);
        Vector2D spritePosition = position + new Vector2D(leaf.Min.X * 0.005, -leaf.Min.Y * 0.005); // Adjusted scaling

        var message = new HudAPIv2.HUDMessage(
            Message: new StringBuilder($"<color={color.R},{color.G},{color.B},{color.A}>■</color>"),
            Origin: spritePosition,
            Scale: scale,
            HideHud: false,
            Shadowing: true,
            ShadowColor: Color.Black,
            Font: "Monospace",
            Blend: BlendTypeEnum.PostPP
        );

        _hudMessages.Add(message);
    }

    private void ClearHUD()
    {
        foreach (var message in _hudMessages)
        {
            message.Visible = false;
            message.DeleteMessage();
        }
        _hudMessages.Clear();
    }

    private static bool IsAPIAlive() => _hudAPI != null && _hudAPI.Heartbeat;
}

// Supporting Classes (unchanged)
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
    }

    public void CreateQuadTrees()
    {
        QuadTreeXNormal.Initialize(_densityXNormal, _maxDensityX);
    }

    public Color GetColor(int value)
    {
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

        for (int y = 0; y < height; y++)
        {
            for (int z = 0; z < width; z++)
            {
                int value = densityMap[y, z];
                if (value > 0)
                {
                    Vector2I min = new Vector2I(y, z);
                    Vector2I max = new Vector2I(y, z);

                    FinishedNodes.Add(new QuadTreeLeaf(min, max, value));
                }
            }
        }
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

    public void GenerateSprites(HudAPIv2 hudAPI, Vector2D topLeftPos, double scale)
    {
        float yOffset = 0;
        foreach (var item in _legendItems.Values)
        {
            var message = new HudAPIv2.HUDMessage(
                Message: new StringBuilder($"<color={item.Color.R},{item.Color.G},{item.Color.B},{item.Color.A}>■</color> {item.Name}"),
                Origin: topLeftPos + new Vector2D(0, yOffset),
                Scale: scale,
                HideHud: false,
                Shadowing: true,
                ShadowColor: Color.Black,
                Font: "Monospace",
                Blend: BlendTypeEnum.PostPP
            );
            yOffset -= 0.05f;
        }
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
    }
}