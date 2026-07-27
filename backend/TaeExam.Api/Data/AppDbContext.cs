using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TaeExam.Api.Models;

namespace TaeExam.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SyllabusChapter> SyllabusChapters => Set<SyllabusChapter>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var stringListComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
            v => v.ToList());

        var intListComparer = new ValueComparer<List<int>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (hash, i) => HashCode.Combine(hash, i)),
            v => v.ToList());

        modelBuilder.Entity<SyllabusChapter>(e =>
        {
            e.HasKey(c => c.Code);
        });

        modelBuilder.Entity<Question>(e =>
        {
            e.HasOne(q => q.ChapterRef).WithMany().HasForeignKey(q => q.Chapter);

            e.Property(q => q.Options)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new())
                .Metadata.SetValueComparer(stringListComparer);

            e.Property(q => q.CorrectIndexes)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new())
                .Metadata.SetValueComparer(intListComparer);
        });

        modelBuilder.Entity<Exam>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>();
        });

        modelBuilder.Entity<ExamQuestion>(e =>
        {
            e.HasOne(eq => eq.Exam).WithMany(x => x.ExamQuestions).HasForeignKey(eq => eq.ExamId);
            e.HasOne(eq => eq.Question).WithMany().HasForeignKey(eq => eq.QuestionId);
        });

        modelBuilder.Entity<Attempt>(e =>
        {
            e.Property(a => a.Status).HasConversion<string>();
            e.HasOne(a => a.Exam).WithMany().HasForeignKey(a => a.ExamId);
        });

        modelBuilder.Entity<AttemptAnswer>(e =>
        {
            e.HasOne(a => a.Attempt).WithMany(x => x.Answers).HasForeignKey(a => a.AttemptId);
            e.HasOne(a => a.ExamQuestion).WithMany().HasForeignKey(a => a.ExamQuestionId);

            e.Property(a => a.SelectedIndexes)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new())
                .Metadata.SetValueComparer(intListComparer);
        });
    }
}
