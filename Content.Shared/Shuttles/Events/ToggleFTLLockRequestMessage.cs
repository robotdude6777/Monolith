using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.Events;

/// <summary>
/// Raised on the client when it wishes to toggle FTL lock for docked shuttles.
/// </summary>
[Serializable, NetSerializable]
public sealed class ToggleFTLLockRequestMessage : BoundUserInterfaceMessage
{
    public IReadOnlyList<NetEntity> DockedEntities { get; }

    public ToggleFTLLockRequestMessage(IReadOnlyList<NetEntity> dockedEntities)
    {
        DockedEntities = dockedEntities;
    }
} 