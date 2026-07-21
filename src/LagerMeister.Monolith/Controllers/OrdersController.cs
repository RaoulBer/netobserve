using LagerMeister.Monolith.Ordering;
using Microsoft.AspNetCore.Mvc;

namespace LagerMeister.Monolith.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orders;

    public OrdersController(OrderService orders)
    {
        _orders = orders;
    }

    // POST /api/orders — multi-step: validate -> reserve stock -> persist.
    // Business rejections (e.g. insufficient stock) surface as 409 so the demo can
    // deterministically produce error traces by ordering more than is on hand.
    [HttpPost]
    public async Task<IActionResult> Place([FromBody] OrderRequest request)
    {
        if (request is null)
            return BadRequest(new { error = "missing body" });

        var result = await _orders.PlaceOrderAsync(request);
        if (result.Ok)
            return Created($"/api/orders/{result.OrderId}", new { orderId = result.OrderId, status = "RESERVED" });

        return result.Rejection switch
        {
            OrderRejection.InsufficientStock => Conflict(new { error = "insufficient_stock" }),
            OrderRejection.UnknownSku => UnprocessableEntity(new { error = "unknown_sku" }),
            _ => BadRequest(new { error = result.Rejection.ToString().ToLowerInvariant() })
        };
    }
}
