using System.Net.Http.Json;
using ShopSphere.Shared;

namespace OrderService;

public interface IProductGateway
{
    Task<ReservationDto> Reserve(ReserveInput input, CancellationToken ct);
    Task Release(Guid id, CancellationToken ct);
}

public class ProductGateway(HttpClient http) : IProductGateway
{
    public async Task<ReservationDto> Reserve(ReserveInput input, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/internal/reservations", input, ct);
        await response.EnsureApiSuccess(ct);
        return (await response.Content.ReadFromJsonAsync<ReservationDto>(ct))!;
    }

    public async Task Release(Guid id, CancellationToken ct)
    {
        using var response = await http.DeleteAsync($"/internal/reservations/{id}", ct);
        await response.EnsureApiSuccess(ct);
    }
}

public interface INotificationGateway
{
    Task Send(NotificationInput input, CancellationToken ct);
}

public class NotificationGateway(HttpClient http) : INotificationGateway
{
    public async Task Send(NotificationInput input, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/internal/notifications", input, ct);
        await response.EnsureApiSuccess(ct);
    }
}
