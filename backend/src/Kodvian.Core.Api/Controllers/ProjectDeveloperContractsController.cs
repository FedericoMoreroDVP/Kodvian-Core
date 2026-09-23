using Kodvian.Core.Api.Validation;
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
public class ProjectDeveloperContractsController : ControllerBase
{
    private readonly IProjectDeveloperContractService _contractService;

    public ProjectDeveloperContractsController(IProjectDeveloperContractService contractService)
    {
        _contractService = contractService;
    }

    [HttpGet("projects/{projectId:guid}/developer-contracts")]
    public async Task<ActionResult<ApiResponseDto<IReadOnlyCollection<ProjectDeveloperContractDto>>>> GetByProject(Guid projectId, CancellationToken cancellationToken)
    {
        var data = await _contractService.GetByProjectAsync(projectId, cancellationToken);
        return Ok(ApiResponseDto<IReadOnlyCollection<ProjectDeveloperContractDto>>.Ok(data, "Contratos obtenidos correctamente"));
    }

    [HttpPost("projects/{projectId:guid}/developer-contracts")]
    [Authorize(Policy = "ProjectsWrite")]
    public async Task<ActionResult<ApiResponseDto<ProjectDeveloperContractDto>>> Create(Guid projectId, [FromBody] ProjectDeveloperContractUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var validationError = RequestValidation.Validate(request);
        if (validationError is not null)
        {
            return BadRequest(ApiResponseDto<ProjectDeveloperContractDto>.Fail(validationError));
        }

        var data = await _contractService.CreateAsync(projectId, request, cancellationToken);
        return Ok(ApiResponseDto<ProjectDeveloperContractDto>.Ok(data, "El contrato se creó correctamente"));
    }

    [HttpPut("developer-contracts/{id:guid}")]
    [Authorize(Policy = "ProjectsWrite")]
    public async Task<ActionResult<ApiResponseDto<ProjectDeveloperContractDto>>> Update(Guid id, [FromBody] ProjectDeveloperContractUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var validationError = RequestValidation.Validate(request);
        if (validationError is not null)
        {
            return BadRequest(ApiResponseDto<ProjectDeveloperContractDto>.Fail(validationError));
        }

        var data = await _contractService.UpdateAsync(id, request, cancellationToken);
        if (data is null)
        {
            return NotFound(ApiResponseDto<ProjectDeveloperContractDto>.Fail("Contrato no encontrado"));
        }

        return Ok(ApiResponseDto<ProjectDeveloperContractDto>.Ok(data, "El contrato se actualizó correctamente"));
    }

    [HttpDelete("developer-contracts/{id:guid}")]
    [Authorize(Policy = "FinancesWrite")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!await _contractService.CancelAsync(id, cancellationToken))
            return NotFound(ApiResponseDto<object>.Fail("Acuerdo no encontrado"));
        return Ok(ApiResponseDto<object>.Ok(new { }, "Acuerdo, pagos y egresos anulados"));
    }

    [HttpGet("developer-contracts/{id:guid}/ledger")]
    public async Task<ActionResult<ApiResponseDto<ContractLedgerDto>>> GetLedger(Guid id, [FromQuery] int year, CancellationToken cancellationToken)
    {
        if (year is < 2000 or > 2100)
        {
            return BadRequest(ApiResponseDto<ContractLedgerDto>.Fail("El año debe estar entre 2000 y 2100"));
        }

        var data = await _contractService.GetLedgerAsync(id, year, cancellationToken);
        if (data is null)
        {
            return NotFound(ApiResponseDto<ContractLedgerDto>.Fail("Contrato no encontrado"));
        }

        return Ok(ApiResponseDto<ContractLedgerDto>.Ok(data, "Ledger del contrato obtenido correctamente"));
    }
}
