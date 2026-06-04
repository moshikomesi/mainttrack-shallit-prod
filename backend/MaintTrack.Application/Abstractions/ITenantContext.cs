using System;

namespace MaintTrack.Application.Abstractions;

/// <summary>
/// Provides access to the current request's tenant identifier.
/// Implementations should be registered as scoped services.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; set; }
}

