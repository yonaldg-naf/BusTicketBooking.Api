using BusTicketBooking.Contexts;
using BusTicketBooking.Dtos.Routes;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace BusTicketBooking.Services
{
    public class RouteService : IRouteService
    {
        private readonly IRepository<BusRoute> _routes;
        private readonly IRepository<RouteStop> _routeStops;
        private readonly IRepository<Stop> _stops;
        private readonly AppDbContext _db; // for efficient includes on reads

        public RouteService(
            IRepository<BusRoute> routes,
            IRepository<RouteStop> routeStops,
            IRepository<Stop> stops,
            AppDbContext db)
        {
            _routes = routes;
            _routeStops = routeStops;
            _stops = stops;
            _db = db;
        }

        public async Task<RouteResponseDto> CreateAsync(CreateRouteRequestDto dto, CancellationToken ct = default)
        {
            ValidateStopsOrdering(dto.Stops);

            // Ensure all StopIds exist
            await EnsureStopsExistAsync(dto.Stops.Select(s => s.StopId).Distinct(), ct);

            // Unique RouteCode per Operator
            var duplicate = (await _routes.FindAsync(r => r.OperatorId == dto.OperatorId && r.RouteCode == dto.RouteCode, ct)).Any();
            if (duplicate)
                throw new InvalidOperationException("RouteCode already exists for this operator.");

            var route = new BusRoute
            {
                OperatorId = dto.OperatorId,
                RouteCode = dto.RouteCode.Trim()
            };

            route = await _routes.AddAsync(route, ct);

            // Create RouteStops
            var rsEntities = dto.Stops
                .OrderBy(s => s.Order)
                .Select(s => new RouteStop
                {
                    RouteId = route.Id,
                    StopId = s.StopId,
                    Order = s.Order,
                    ArrivalOffsetMin = s.ArrivalOffsetMin,
                    DepartureOffsetMin = s.DepartureOffsetMin
                }).ToList();

            await _routeStops.AddRangeAsync(rsEntities, ct);

            // Load with stops for response
            return await GetByIdAsync(route.Id, ct) ?? throw new InvalidOperationException("Route creation failed to load.");
        }

        public async Task<IEnumerable<RouteResponseDto>> GetAllAsync(CancellationToken ct = default)
        {
            // Efficient projection with include
            var data = await _db.BusRoutes
                .Include(r => r.RouteStops)
                    .ThenInclude(rs => rs.Stop!)
                .AsNoTracking()
                .ToListAsync(ct);

            return data.Select(MapRoute);
        }

        public async Task<RouteResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            var route = await _db.BusRoutes
                .Include(r => r.RouteStops)
                    .ThenInclude(rs => rs.Stop!)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, ct);

            return route is null ? null : MapRoute(route);
        }

        public async Task<RouteResponseDto?> UpdateAsync(Guid id, UpdateRouteRequestDto dto, CancellationToken ct = default)
        {
            ValidateStopsOrdering(dto.Stops);
            await EnsureStopsExistAsync(dto.Stops.Select(s => s.StopId).Distinct(), ct);

            var route = await _routes.GetByIdAsync(id, ct);
            if (route is null) return null;

            // Check unique RouteCode per operator (exclude current)
            var dup = (await _routes.FindAsync(r => r.OperatorId == route.OperatorId && r.RouteCode == dto.RouteCode && r.Id != id, ct)).Any();
            if (dup)
                throw new InvalidOperationException("RouteCode already exists for this operator.");

            route.RouteCode = dto.RouteCode.Trim();
            route.UpdatedAtUtc = DateTime.UtcNow;
            await _routes.UpdateAsync(route, ct);

            // Replace route stops for simplicity
            var existing = await _routeStops.FindAsync(rs => rs.RouteId == id, ct);
            if (existing.Any())
                await _routeStops.RemoveRangeAsync(existing, ct);

            var newStops = dto.Stops
                .OrderBy(s => s.Order)
                .Select(s => new RouteStop
                {
                    RouteId = id,
                    StopId = s.StopId,
                    Order = s.Order,
                    ArrivalOffsetMin = s.ArrivalOffsetMin,
                    DepartureOffsetMin = s.DepartureOffsetMin
                }).ToList();

            await _routeStops.AddRangeAsync(newStops, ct);

            return await GetByIdAsync(id, ct);
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            var route = await _routes.GetByIdAsync(id, ct);
            if (route is null) return false;

            // Remove children first due to FK constraints (we set cascade in model, but be explicit)
            var rs = await _routeStops.FindAsync(s => s.RouteId == id, ct);
            if (rs.Any())
                await _routeStops.RemoveRangeAsync(rs, ct);

            await _routes.RemoveAsync(route, ct);
            return true;
        }

        private static void ValidateStopsOrdering(IEnumerable<RouteStopItemDto> stops)
        {
            var ordered = stops.OrderBy(s => s.Order).ToList();
            if (ordered.Count < 2)
                throw new InvalidOperationException("A route must contain at least two stops.");

            // Ensure strictly 1..n continuous
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Order != i + 1)
                    throw new InvalidOperationException("Stop orders must be continuous starting at 1.");
            }

            // No duplicate StopId at same order (and generally allow revisits only if needed; we disallow for v1)
            var dupOrder = ordered.GroupBy(s => s.Order).Any(g => g.Count() > 1);
            if (dupOrder) throw new InvalidOperationException("Duplicate 'Order' values are not allowed.");

            var dupStop = ordered.GroupBy(s => s.StopId).Any(g => g.Count() > 1);
            if (dupStop) throw new InvalidOperationException("A stop cannot appear more than once in a route for v1.");
        }

        private async Task EnsureStopsExistAsync(IEnumerable<Guid> stopIds, CancellationToken ct)
        {
            var ids = stopIds.Distinct().ToList();
            if (!ids.Any()) throw new InvalidOperationException("No stops specified.");

            var found = await _db.Stops.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct);
            if (found.Count != ids.Count)
            {
                var missing = string.Join(", ", ids.Except(found));
                throw new InvalidOperationException($"One or more StopIds do not exist: {missing}");
            }
        }

        private static RouteResponseDto MapRoute(BusRoute route)
        {
            var dto = new RouteResponseDto
            {
                Id = route.Id,
                OperatorId = route.OperatorId,
                RouteCode = route.RouteCode,
                CreatedAtUtc = route.CreatedAtUtc,
                UpdatedAtUtc = route.UpdatedAtUtc,
                Stops = route.RouteStops
                    .OrderBy(rs => rs.Order)
                    .Select(rs => new RouteStopViewDto
                    {
                        StopId = rs.StopId,
                        Order = rs.Order,
                        ArrivalOffsetMin = rs.ArrivalOffsetMin,
                        DepartureOffsetMin = rs.DepartureOffsetMin,
                        City = rs.Stop?.City ?? string.Empty,
                        Name = rs.Stop?.Name ?? string.Empty
                    }).ToList()
            };
            return dto;
        }
    }
}