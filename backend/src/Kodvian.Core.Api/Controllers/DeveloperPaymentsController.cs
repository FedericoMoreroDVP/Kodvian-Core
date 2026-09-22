using System.Security.Claims;
using Kodvian.Core.Api.Validation;
using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Developers.Abstractions;
using Kodvian.Core.Application.Developers.Dtos;
using Kodvian.Core.Application.Developers.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdministratorOnly")]
[Route("api")]
public class DeveloperPaymentsController : ControllerBase
{
    private readonly IDeveloperPaymentService _paymentService;

    public DeveloperPaymentsController(IDeveloperPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("developer-contracts/{contractId:guid}/payments")]
    public async Task<ActionResult<ApiResponseDto<IReadOnlyCollection<DeveloperPaymentDto>>>> GetByContract(Guid contractId, CancellationToken cancellationToken)
    {
        var data = await _paymentService.GetByContractAsync(contractId, cancellationToken);
        return Ok(ApiResponseDto<IReadOnlyCollection<DeveloperPaymentDto>>.Ok(data, "Pagos obtenidos correctamente"));
    }

    [HttpPost("developer-contracts/{contractId:guid}/payments")]
    [Authorize(Policy = "FinancesWrite")]
    public async Task<ActionResult<ApiResponseDto<DeveloperPaymentDto>>> Create(Guid contractId, [FromBody] DeveloperPaymentCreateRequestDto request, CancellationToken cancellationToken)
    {
        var validationError = RequestValidation.Validate(request);
        if (validationError is not null)
        {
            return BadRequest(ApiResponseDto<DeveloperPaymentDto>.Fail(validationError));
        }

        var data = await _paymentService.CreateAsync(contractId, request, cancellationToken);
        return Ok(ApiResponseDto<DeveloperPaymentDto>.Ok(data, "El pago se registró correctamente"));
    }

    [HttpPut("developer-payments/{id:guid}"), Authorize(Policy = "FinancesWrite")]
    public async Task<IActionResult> Update(Guid id, DeveloperPaymentCreateRequestDto request, CancellationToken ct) =>
        Ok(ApiResponseDto<DeveloperPaymentDto>.Ok(await _paymentService.UpdateAsync(id, request, ct), "Pago y egreso actualizados"));

    [HttpDelete("developer-payments/{id:guid}"), Authorize(Policy = "FinancesWrite")]
    public async Task<IActionResult> Cancel(Guid id, [FromQuery] Guid expectedVersion, CancellationToken ct)
    {
        await _paymentService.CancelAsync(id, expectedVersion, ct);
        return Ok(ApiResponseDto<object>.Ok(new { }, "Pago y egreso anulados"));
    }

    [HttpGet("developer-payments/{paymentId:guid}/receipts")]
    public async Task<ActionResult<ApiResponseDto<IReadOnlyCollection<FileMetadataDto>>>> GetReceipts(Guid paymentId, CancellationToken cancellationToken)
    {
        var data = await _paymentService.GetReceiptsAsync(paymentId, cancellationToken);
        return Ok(ApiResponseDto<IReadOnlyCollection<FileMetadataDto>>.Ok(data, "Comprobantes obtenidos correctamente"));
    }

    [HttpPost("developer-payments/{paymentId:guid}/receipts")]
    [Authorize(Policy = "FinancesWrite")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponseDto<FileMetadataDto>>> UploadReceipt(Guid paymentId, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponseDto<FileMetadataDto>.Fail("Debes seleccionar un archivo"));
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var uploadedById))
        {
            return Unauthorized(ApiResponseDto<FileMetadataDto>.Fail("Token inválido"));
        }

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        var data = await _paymentService.AddReceiptAsync(paymentId, uploadedById, file.FileName, file.ContentType, memory.ToArray(), cancellationToken);
        return Ok(ApiResponseDto<FileMetadataDto>.Ok(data, "Comprobante cargado correctamente"));
    }

    [HttpGet("developer-payments/{paymentId:guid}/receipts/{receiptId:guid}")]
    public async Task<IActionResult> DownloadReceipt(Guid paymentId, Guid receiptId, CancellationToken cancellationToken)
    {
        var file = await _paymentService.GetReceiptContentAsync(paymentId, receiptId, cancellationToken);
        if (file is null)
        {
            return NotFound(ApiResponseDto<object>.Fail("Comprobante no encontrado"));
        }

        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("developer-payments/{paymentId:guid}/receipts/{receiptId:guid}")]
    [Authorize(Policy = "FinancesWrite")]
    public async Task<ActionResult<ApiResponseDto<object>>> DeleteReceipt(Guid paymentId, Guid receiptId, CancellationToken cancellationToken)
    {
        var deleted = await _paymentService.DeleteReceiptAsync(paymentId, receiptId, cancellationToken);
        if (!deleted)
        {
            return NotFound(ApiResponseDto<object>.Fail("Comprobante no encontrado"));
        }

        return Ok(ApiResponseDto<object>.Ok(new { }, "Comprobante eliminado correctamente"));
    }
}
