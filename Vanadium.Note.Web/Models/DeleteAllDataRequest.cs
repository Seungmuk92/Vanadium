namespace Vanadium.Note.Web.Models;

/// <summary>
/// Request body for the all-data purge. Mirrors the REST DTO — the owner password is
/// re-confirmed server-side before anything is deleted (issue #289).
/// </summary>
public record DeleteAllDataRequest(string Password);
