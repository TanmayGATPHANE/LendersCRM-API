using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// Excel lending-book import (plan §4.7). Preview parses and returns the
/// model for the dialog's review table; the commit endpoint re-parses the
/// same bytes and writes. Both take the file as multipart form-data and
/// the rate (%/month) on the query string — the dialog's field defaults
/// to the template's 2 %/month.
[ApiController]
[Route("api/import")]
[Authorize(Policy = "Writer")]
public class ImportController(IExcelImportService imports) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(IFormFile file, [FromQuery] decimal rate = 2)
    {
        if (file is null || file.Length == 0)
            throw ApiException.BadRequest("Choose an .xlsx file to import");
        await using var stream = file.OpenReadStream();
        return Ok(await imports.PreviewAsync(stream, file.FileName));
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit(IFormFile file, [FromQuery] decimal rate = 2)
    {
        if (file is null || file.Length == 0)
            throw ApiException.BadRequest("Choose an .xlsx file to import");
        await using var stream = file.OpenReadStream();
        var result = await imports.CommitAsync(stream, file.FileName, rate, CurrentUser.Actor(User));
        return Ok(result);
    }
}
