using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Finances.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kodvian.Core.Api.Controllers;

[ApiController, Route("api/finance"), Authorize(Policy = "AdministratorOnly")]
public class FinanceController(IFinanceOverviewService service) : ControllerBase
{
    [HttpGet("team-obligations")]
    public async Task<IActionResult> Obligations([FromQuery] int year, [FromQuery] PagedRequestDto request, CancellationToken ct) =>
        Ok(ApiResponseDto<PagedResultDto<Kodvian.Core.Application.Developers.Dtos.ContractLedgerDto>>.Ok(await service.TeamObligationsAsync(year, request, ct), "Saldos del equipo obtenidos"));
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(DateOnly? from, DateOnly? to, CancellationToken ct) => Ok(ApiResponseDto<FinanceOverviewDto>.Ok(await service.GetAsync(from, to, ct), "Situación financiera obtenida"));
    [HttpPut("setup"), Authorize(Policy = "FinancesWrite")]
    public async Task<IActionResult> Setup(FinanceSetupDto request, CancellationToken ct) => Ok(ApiResponseDto<FinanceSetupDto>.Ok(await service.SetupAsync(request, ct), "Configuración guardada"));
    [HttpGet("partners")]
    public async Task<IActionResult> Partners(CancellationToken ct) => Ok(ApiResponseDto<IReadOnlyCollection<PartnerDto>>.Ok(await service.PartnersAsync(ct), "Socios obtenidos"));
    [HttpPost("partners"), Authorize(Policy = "FinancesWrite")]
    public async Task<IActionResult> CreatePartner(PartnerRequest request, CancellationToken ct) => Ok(ApiResponseDto<PartnerDto>.Ok(await service.SavePartnerAsync(null, request, ct), "Socio guardado"));
    [HttpPut("partners/{id:guid}"), Authorize(Policy = "FinancesWrite")]
    public async Task<IActionResult> UpdatePartner(Guid id, PartnerRequest request, CancellationToken ct) => Ok(ApiResponseDto<PartnerDto>.Ok(await service.SavePartnerAsync(id, request, ct), "Socio guardado"));
    [HttpPost("exchanges"), Authorize(Policy = "FinancesWrite")]
    public async Task<IActionResult> Exchange(ExchangeRequest request, CancellationToken ct) => Ok(ApiResponseDto<Guid>.Ok(await service.ExchangeAsync(request, ct), "Cambio registrado"));
    [HttpDelete("exchanges/{id:guid}"), Authorize(Policy = "FinancesWrite")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) { await service.CancelExchangeAsync(id, ct); return Ok(ApiResponseDto<object>.Ok(new { }, "Cambio anulado")); }
}
