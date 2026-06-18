using GiseBsPayGateway.Constants;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Extensions;
using GiseBsPayGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiseBsPayGateway.Controllers.Api;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly ICatalogService _catalogService;

    public ProductsController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet("options")]
    public ActionResult<CatalogOptions.CatalogOptionsResponse> GetOptions()
    {
        return Ok(new CatalogOptions.CatalogOptionsResponse(CatalogOptions.PlanCodes, CatalogOptions.Currencies));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> List(CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var products = await _catalogService.ListProductsAsync(app, cancellationToken);
        return Ok(products);
    }

    [HttpGet("{productCode}")]
    public async Task<ActionResult<ProductResponse>> Get(string productCode, CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        var product = await _catalogService.GetProductAsync(app, productCode, cancellationToken);
        return product is null
            ? NotFound(new ApiErrorResponse($"Produit '{productCode}' introuvable.", null))
            : Ok(product);
    }

    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            var product = await _catalogService.CreateProductAsync(app, request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { productCode = product.ProductCode }, product);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }

    /// <summary>Crée un produit et un plan tarifaire en une seule requête (catalogue complet).</summary>
    [HttpPost("catalog")]
    public async Task<ActionResult<CatalogItemResponse>> CreateCatalogItem(
        [FromBody] CreateCatalogItemRequest request,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            var item = await _catalogService.CreateCatalogItemAsync(app, request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { productCode = item.Product.ProductCode }, item);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }

    [HttpPost("{productCode}/plans")]
    public async Task<ActionResult<PricingPlanResponse>> CreatePlan(
        string productCode,
        [FromBody] CreatePricingPlanRequest request,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            var plan = await _catalogService.CreatePlanAsync(app, productCode, request, cancellationToken);
            return Created($"/api/products/{productCode}", plan);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }

    [HttpPost("{productCode}/sync-stripe")]
    public async Task<ActionResult<ProductResponse>> SyncStripe(
        string productCode,
        CancellationToken cancellationToken)
    {
        var app = HttpContext.GetClientApplicationContext().Application;
        try
        {
            var product = await _catalogService.SyncProductToStripeAsync(app, productCode, cancellationToken);
            return Ok(product);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.Message, null));
        }
    }
}
