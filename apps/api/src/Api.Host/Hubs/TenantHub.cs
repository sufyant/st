using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Api.Host.Hubs;

[Authorize]
public sealed class TenantHub : Hub;
