using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

public class LabelService(NoteDbContext db, ILogger<LabelService> logger)
{
    // ── Categories ────────────────────────────────────────────────────────────

    public async Task<List<LabelCategoryDto>> GetAllCategoriesAsync()
    {
        var categories = await db.LabelCategories
            .Include(c => c.Labels)
            .OrderBy(c => c.Name)
            .ToListAsync();

        return categories.Select(c => new LabelCategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            Labels = c.Labels
                .Select(l => new LabelSummary { Id = l.Id, Name = l.Name, CategoryId = l.CategoryId, CategoryName = c.Name })
                .OrderBy(l => l.Name)
                .ToList()
        }).ToList();
    }

    public async Task<LabelCategoryDto> CreateCategoryAsync(string name)
    {
        var duplicate = await db.LabelCategories
            .AnyAsync(c => c.Name.ToLower() == name.ToLower());
        if (duplicate)
            throw new InvalidOperationException($"Category '{name}' already exists.");

        var category = new LabelCategory { Name = name };
        db.LabelCategories.Add(category);
        await db.SaveChangesAsync();
        logger.LogInformation("Label category created: {CategoryId} ({CategoryName}).", category.Id, category.Name);
        return new LabelCategoryDto { Id = category.Id, Name = category.Name };
    }

    public async Task<bool> DeleteCategoryAsync(Guid id)
    {
        var category = await db.LabelCategories.FindAsync(id);
        if (category is null)
        {
            logger.LogDebug("Label category {CategoryId} not found for deletion.", id);
            return false;
        }
        db.LabelCategories.Remove(category);
        await db.SaveChangesAsync();
        logger.LogInformation("Label category deleted: {CategoryId} ({CategoryName}).", id, category.Name);
        return true;
    }

    // ── Labels ────────────────────────────────────────────────────────────────

    public async Task<List<LabelSummary>> GetAllLabelsAsync() =>
        await db.Labels
            .Include(l => l.Category)
            .OrderBy(l => l.Name)
            .Select(l => new LabelSummary
            {
                Id = l.Id,
                Name = l.Name,
                CategoryId = l.CategoryId,
                CategoryName = l.Category != null ? l.Category.Name : null
            })
            .ToListAsync();

    public async Task<LabelSummary> CreateLabelAsync(string name, Guid? categoryId)
    {
        var duplicate = await db.Labels
            .AnyAsync(l => l.Name.ToLower() == name.ToLower());
        if (duplicate)
            throw new InvalidOperationException($"Label '{name}' already exists.");

        var label = new Label { Name = name, CategoryId = categoryId };
        db.Labels.Add(label);
        await db.SaveChangesAsync();

        string? categoryName = null;
        if (categoryId.HasValue)
            categoryName = (await db.LabelCategories.FindAsync(categoryId.Value))?.Name;

        logger.LogInformation("Label created: {LabelId} ({LabelName}), CategoryId: {CategoryId}.",
            label.Id, label.Name, categoryId);
        return new LabelSummary { Id = label.Id, Name = label.Name, CategoryId = categoryId, CategoryName = categoryName };
    }

    public async Task<bool> DeleteLabelAsync(Guid id)
    {
        var label = await db.Labels.FindAsync(id);
        if (label is null)
        {
            logger.LogDebug("Label {LabelId} not found for deletion.", id);
            return false;
        }
        db.Labels.Remove(label);
        await db.SaveChangesAsync();
        logger.LogInformation("Label deleted: {LabelId} ({LabelName}).", id, label.Name);
        return true;
    }

    // ── Note-Label assignments ────────────────────────────────────────────────

    public async Task AddLabelToNoteAsync(Guid noteId, Guid labelId)
    {
        var exists = await db.NoteLabels.AnyAsync(nl => nl.NoteId == noteId && nl.LabelId == labelId);
        if (exists)
        {
            logger.LogDebug("Label {LabelId} already assigned to note {NoteId}.", labelId, noteId);
            return;
        }

        var label = await db.Labels
            .Include(l => l.Category)
            .FirstOrDefaultAsync(l => l.Id == labelId)
            ?? throw new KeyNotFoundException("Label not found");

        // Category constraint: remove any existing label from the same category
        if (label.CategoryId is not null)
        {
            var conflicting = await db.NoteLabels
                .Include(nl => nl.Label)
                .Where(nl => nl.NoteId == noteId && nl.Label.CategoryId == label.CategoryId)
                .ToListAsync();

            if (conflicting.Count > 0)
            {
                logger.LogInformation(
                    "Category constraint: removing {Count} conflicting label(s) from note {NoteId} " +
                    "(category {CategoryId}) before assigning label {LabelId}.",
                    conflicting.Count, noteId, label.CategoryId, labelId);
                db.NoteLabels.RemoveRange(conflicting);
            }
        }

        db.NoteLabels.Add(new NoteLabel { NoteId = noteId, LabelId = labelId });
        await db.SaveChangesAsync();
        logger.LogInformation("Label {LabelId} assigned to note {NoteId}.", labelId, noteId);
    }

    public async Task<bool> RemoveLabelFromNoteAsync(Guid noteId, Guid labelId)
    {
        var noteLabel = await db.NoteLabels.FindAsync(noteId, labelId);
        if (noteLabel is null)
        {
            logger.LogDebug("Label {LabelId} not assigned to note {NoteId}.", labelId, noteId);
            return false;
        }
        db.NoteLabels.Remove(noteLabel);
        await db.SaveChangesAsync();
        logger.LogInformation("Label {LabelId} removed from note {NoteId}.", labelId, noteId);
        return true;
    }
}
