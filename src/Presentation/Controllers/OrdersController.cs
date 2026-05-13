namespace Czanix.Presentation.Controllers;

using Czanix.Application.DTOs;
using Czanix.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/[controller]")]
public class OrdersController(CreateOrderUseCase createOrder) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderInput input, CancellationToken ct)
    {
        var result = await createOrder.ExecuteAsync(input, ct);

        return result.Match<IActionResult>(
            onSuccess: value => Created($"/api/v1/orders/{value.PublicId}", value),
            onFailure: error => UnprocessableEntity(new { error })
        );
    }
}
