using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShopSphere.Shared;

namespace OrderService;

[ApiController, Route("api/orders"), Authorize]
public class OrdersController(OrderLogic logic) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Place(PlaceOrderInput input, CancellationToken ct)
    {
        var o = await logic.Create(input, User.UserId(), User.FindFirst("email")?.Value ?? "", ct);
        return o.Status == "PendingInventory"
            ? AcceptedAtAction(nameof(Get), new { id = o.Id }, o)
            : CreatedAtAction(nameof(Get), new { id = o.Id }, o);
    }

    [HttpGet("{id:guid}")]
    public Task<OrderDto> Get(Guid id, CancellationToken ct) =>
        logic.Get(id, User.UserId(), User.IsInRole("Administrator"), ct);

    [HttpGet]
    public Task<Page<OrderDto>> History(
        int page = 1,
        int pageSize = 20,
        bool all = false,
        CancellationToken ct = default
    ) => logic.History(User.UserId(), all && User.IsInRole("Administrator"), page, pageSize, ct);

    [HttpPut("{id:guid}/status"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Status(Guid id, StatusInput input, CancellationToken ct)
    {
        await logic.SetStatus(id, input.Status, ct);
        return NoContent();
    }
}
