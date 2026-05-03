using ExperionAgent.Core.Models;

namespace ExperionAgent.Core.Interfaces;

public interface IActivityMiner
{
    Task<ActivityIngestResponse> IngestAsync(
        ActivityIngestRequest request, string resolvedUserId, GeoLocation? location,
        CancellationToken ct = default);
}
