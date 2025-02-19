﻿using Content.Server.Station.Systems;

namespace Content.Server.Station.Components;

/// <summary>
/// Stores core information about a station, namely it's config and associated grids.
/// All station entities will have this component.
/// </summary>
[RegisterComponent, Access(typeof(StationSystem))]
public sealed class StationDataComponent : Component
{
    /// <summary>
    /// The game map prototype, if any, associated with this station.
    /// </summary>
    [DataField("stationConfig")]
    public StationConfig? StationConfig = null;

    /// <summary>
    /// List of all grids this station is part of.
    /// </summary>
    /// <remarks>
    /// You should not mutate this yourself, go through StationSystem so the appropriate events get fired.
    /// </remarks>
    [DataField("grids")]
    public readonly HashSet<EntityUid> Grids = new();
}
