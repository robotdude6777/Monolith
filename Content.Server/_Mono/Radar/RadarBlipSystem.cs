using System.Numerics;
using Content.Server.Theta.ShipEvent.Components;
using Content.Shared._Mono.Radar;
using Content.Shared.Projectiles;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;

namespace Content.Server._Mono.Radar;

public sealed partial class RadarBlipSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RequestBlipsEvent>(OnBlipsRequested);
    }

    private void OnBlipsRequested(RequestBlipsEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetEntity(ev.Radar, out var radarUid))
            return;

        if (!TryComp<RadarConsoleComponent>(radarUid, out var radar))
            return;

        var blips = AssembleBlipsReport((EntityUid)radarUid, radar);

        var giveEv = new GiveBlipsEvent(ev.Radar, blips);
        RaiseNetworkEvent(giveEv, args.SenderSession);
    }

    private List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> AssembleBlipsReport(EntityUid uid, RadarConsoleComponent? component = null)
    {
        var blips = new List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)>();

        if (!Resolve(uid, ref component))
            return blips;

        var radarXform = Transform(uid);
        var radarPosition = _xform.GetWorldPosition(uid);
        var radarGrid = _xform.GetGrid(uid);
        var radarMapId = radarXform.MapID;

        var blipQuery = EntityQueryEnumerator<RadarBlipComponent, TransformComponent>();

        while (blipQuery.MoveNext(out var blipUid, out var blip, out var blipXform))
        {
            if (!blip.Enabled)
                continue;

            // Don't show radar blips for projectiles on different maps than the one they were fired from
            if (ShouldSkipProjectileBlip(blipUid, blipXform, radarMapId))
                continue;

            var blipPosition = _xform.GetWorldPosition(blipUid);
            var distance = (blipPosition - radarPosition).Length();
            if (distance > component.MaxRange)
                continue;

            var blipGrid = _xform.GetGrid(blipUid);

            // Check if this is a shield radar blip without a grid
            // If so, don't display it (fixes grid-orphaned shield generators)
            if (HasComp<CircularShieldRadarComponent>(blipUid) && blipGrid == null)
                continue;

            if (blip.RequireNoGrid)
            {
                if (blipGrid != null)
                    continue;

                // For free-floating blips without a grid, use world position with null grid
                blips.Add((null, blipPosition, blip.Scale, blip.RadarColor, blip.Shape));
            }
            else if (blip.VisibleFromOtherGrids)
            {
                // For blips that should be visible from other grids, add them regardless of grid
                AddGridOrWorldBlip(blips, blipGrid, blipPosition, blip);
            }
            else
            {
                // If we're requiring grid, make sure they're on the same grid
                if (blipGrid != radarGrid)
                    continue;

                // Add the blip using the shared helper method
                AddGridOrWorldBlip(blips, blipGrid, blipPosition, blip);
            }
        }

        return blips;
    }

    /// <summary>
    /// Determines if a projectile blip should be skipped based on map context.
    /// </summary>
    private bool ShouldSkipProjectileBlip(EntityUid blipUid, TransformComponent blipXform, MapId radarMapId)
    {
        if (TryComp<ProjectileComponent>(blipUid, out var projectile))
        {
            // If the projectile is on a different map than the radar, don't show it
            if (blipXform.MapID != radarMapId)
                return true;

            // If we can determine the shooter and they're on a different map, don't show the blip
            if (projectile.Shooter != null &&
                TryComp<TransformComponent>(projectile.Shooter, out var shooterXform) &&
                shooterXform.MapID != blipXform.MapID)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds a blip to the list, converting to grid-local coordinates if needed.
    /// </summary>
    private void AddGridOrWorldBlip(
        List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> blips,
        EntityUid? blipGrid,
        Vector2 blipPosition,
        RadarBlipComponent blip)
    {
        if (blipGrid != null)
        {
            // Local position relative to grid
            var gridMatrix = _xform.GetWorldMatrix(blipGrid.Value);
            Matrix3x2.Invert(gridMatrix, out var invGridMatrix);
            var localPos = Vector2.Transform(blipPosition, invGridMatrix);

            // Add grid-relative blip with grid entity ID
            blips.Add((GetNetEntity(blipGrid.Value), localPos, blip.Scale, blip.RadarColor, blip.Shape));
        }
        else
        {
            // Fallback to world position with null grid
            blips.Add((null, blipPosition, blip.Scale, blip.RadarColor, blip.Shape));
        }
    }
}
