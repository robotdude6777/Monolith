using System.Linq;
using System.Numerics;
using Content.Shared._Mono.Radar;
using Robust.Shared.Timing;

namespace Content.Client._Mono.Radar;

public sealed partial class RadarBlipsSystem : EntitySystem
{
    private const double BlipStaleSeconds = 3.0; // Time after which blips are considered stale

    // Cache collections to avoid allocations
    private static readonly List<(Vector2, float, Color, RadarBlipShape)> EmptyBlipList = new();
    private static readonly List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> EmptyRawBlipList = new();

    // Request throttling
    private TimeSpan _lastRequestTime = TimeSpan.Zero;
    private static readonly TimeSpan RequestThrottle = TimeSpan.FromMilliseconds(250);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private readonly Dictionary<NetEntity, (TimeSpan Time, List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)>)> _receivedBlips = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GiveBlipsEvent>(OnBlipsReceived);
    }

    private void OnBlipsReceived(GiveBlipsEvent ev, EntitySessionEventArgs args)
    {
        var currentTime = _timing.CurTime;
        if (ev.Blips == null)
            return;

        var fromRadar = ev.FromRadar != default ? ev.FromRadar : GetNetEntity(EntityUid.Invalid);
        _receivedBlips[fromRadar] = (currentTime, ev.Blips);
    }

    /// <summary>
    /// Request blips from the server if sufficient time has passed since the last request.
    /// </summary>
    public void RequestBlips(EntityUid console)
    {
        // Only request if we have a valid console
        if (!Exists(console))
            return;

        var currentTime = _timing.CurTime;
        
        // Throttle requests to avoid network spam
        if (currentTime - _lastRequestTime < RequestThrottle)
            return;
        
        _lastRequestTime = currentTime;
        var netConsole = GetNetEntity(console);
        var ev = new RequestBlipsEvent(netConsole);
        RaiseNetworkEvent(ev);
    }
    
    /// <summary>
    /// Request blips from the server using a NetEntity directly.
    /// </summary>
    public void RequestBlips(NetEntity console)
    {
        var currentTime = _timing.CurTime;
        
        // Throttle requests to avoid network spam
        if (currentTime - _lastRequestTime < RequestThrottle)
            return;
        
        _lastRequestTime = currentTime;
        var ev = new RequestBlipsEvent(console);
        RaiseNetworkEvent(ev);
    }

    /// <summary>
    /// Gets the current blips as world positions with their scale, color and shape.
    /// This is needed for the legacy radar display that expects world coordinates.
    /// </summary>
    public List<(Vector2, float, Color, RadarBlipShape)> GetCurrentBlips()
    {
        // For backwards compatibility, try to use any available radar data
        if (_receivedBlips.Count == 0)
            return EmptyBlipList;

        // Just use the first radar's data if there's no specific one requested
        var firstRadar = _receivedBlips.First().Key;
        return GetCurrentBlips(firstRadar);
    }

    /// <summary>
    /// Gets the current blips for a specific radar entity as world positions with their scale, color and shape.
    /// </summary>
    public List<(Vector2, float, Color, RadarBlipShape)> GetCurrentBlips(EntityUid radar)
    {
        return GetCurrentBlips(GetNetEntity(radar));
    }

    /// <summary>
    /// Gets the current blips for a specific radar as world positions with their scale, color and shape.
    /// </summary>
    public List<(Vector2, float, Color, RadarBlipShape)> GetCurrentBlips(NetEntity radar)
    {
        // Check if data is stale
        if (!_receivedBlips.TryGetValue(radar, out var blipsData) ||
            (_timing.CurTime - blipsData.Time).TotalSeconds > BlipStaleSeconds)
            return EmptyBlipList;

        var result = new List<(Vector2, float, Color, RadarBlipShape)>(blipsData.Item2.Count);

        foreach (var blip in blipsData.Item2)
        {
            // If no grid, position is already in world coordinates
            if (blip.Grid == null)
            {
                result.Add((blip.Position, blip.Scale, blip.Color, blip.Shape));
                continue;
            }

            // If grid exists, transform from grid-local to world coordinates
            if (TryGetEntity(blip.Grid, out var gridEntity))
            {
                // Transform the grid-local position to world position
                var worldPos = _xform.GetWorldPosition(gridEntity.Value);
                var gridRot = _xform.GetWorldRotation(gridEntity.Value);

                // Rotate the local position by grid rotation and add grid position
                var rotatedLocalPos = gridRot.RotateVec(blip.Position);
                var finalWorldPos = worldPos + rotatedLocalPos;

                result.Add((finalWorldPos, blip.Scale, blip.Color, blip.Shape));
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the raw blips data which includes grid information for more accurate rendering.
    /// </summary>
    public List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> GetRawBlips()
    {
        // For backwards compatibility, try to use any available radar data
        if (_receivedBlips.Count == 0)
            return EmptyRawBlipList;

        // Just use the first radar's data if there's no specific one requested
        var firstRadar = _receivedBlips.First().Key;
        return GetRawBlips(firstRadar);
    }

    /// <summary>
    /// Gets the raw blips data for a specific radar entity, which includes grid information for more accurate rendering.
    /// </summary>
    public List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> GetRawBlips(EntityUid radar)
    {
        return GetRawBlips(GetNetEntity(radar));
    }

    /// <summary>
    /// Gets the raw blips data for a specific radar, which includes grid information for more accurate rendering.
    /// </summary>
    public List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> GetRawBlips(NetEntity radar)
    {
        // Check if data is stale
        if (!_receivedBlips.TryGetValue(radar, out var blipsData) ||
            (_timing.CurTime - blipsData.Time).TotalSeconds > BlipStaleSeconds)
            return EmptyRawBlipList;

        return blipsData.Item2;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _receivedBlips.Clear();
    }
}
