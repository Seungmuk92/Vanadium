using System.ComponentModel.DataAnnotations;

namespace Vanadium.Note.REST.Models;

/// <summary>
/// Request body for the all-data purge. The owner password is re-confirmed server-side
/// before anything is deleted, so a leaked token alone cannot wipe the account (issue #289).
/// </summary>
public record DeleteAllDataRequest(
    [Required][MaxLength(256)] string Password
);
