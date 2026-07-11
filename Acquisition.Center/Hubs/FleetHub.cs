using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Acquisition.Center.Hubs;

[Authorize(Policy = "Viewer")]
public sealed class FleetHub : Hub { }
