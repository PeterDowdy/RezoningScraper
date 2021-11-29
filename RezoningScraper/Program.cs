﻿using RezoningScraper;
using Spectre.Console;
using System.Diagnostics;
using System.Reflection;
using static Spectre.Console.AnsiConsole;

MarkupLine($"[green]Welcome to RezoningScraper v{Assembly.GetExecutingAssembly().GetName().Version}[/]");
WriteLine();

await AnsiConsole.Status().StartAsync("Opening DB...", async ctx => 
{
    try
    {
        var db = DbHelper.CreateOrOpenFileDb("RezoningScraper.db");
        db.InitializeSchemaIfNeeded();

        ctx.Status = "Loading token...";
        var token = await TokenHelper.GetTokenFromDbOrWebsite(db);

        ctx.Status = "Querying API...";
        WriteLine("Starting API query...");
        var stopwatch = Stopwatch.StartNew();
        var latestProjects = await API.GetAllProjects(token.JWT).ToListAsync();
        MarkupLine($"API query finished: retrieved {latestProjects.Count} projects in [yellow]{stopwatch.ElapsedMilliseconds}ms[/]");

        ctx.Status = "Comparing against projects in local database...";
        stopwatch.Restart();
        List<Project> newProjects = new();
        List<ChangedProject> changedProjects = new();
        var tran = db.BeginTransaction();
        foreach (var project in latestProjects)
        {
            if (db.ContainsProject(project))
            {
                var oldVersion = db.GetProject(project.id!);
                var comparer = new ProjectComparer(oldVersion, project);

                if (comparer.DidProjectChange(out var changes))
                {
                    changedProjects.Add(new (oldVersion, project, changes));
                }
            }
            else
            {
                newProjects.Add(project);
            }

            db.UpsertProject(project);
        }
        tran.Commit();

        MarkupLine($"Upserted {latestProjects.Count} projects to the DB in [yellow]{stopwatch.ElapsedMilliseconds}ms[/]");
        MarkupLine($"Found [green]{newProjects.Count}[/] new projects and [green]{changedProjects.Count}[/] modified projects.");

        // TODO: post changes to Slack

        PrintNewProjects(newProjects);
        PrintChangedProjects(changedProjects);
    }
    catch (Exception ex)
    {
        MarkupLine("[red]Fatal exception thrown[/]");
        WriteException(ex);
    }
});

void PrintNewProjects(List<Project> newProjects)
{
    if (newProjects.Count == 0) return;

    WriteLine();
    MarkupLine("[bold underline green]New Projects[/]");
    WriteLine();

    foreach (var project in newProjects)
    {
        MarkupLine($"[bold underline]{project.attributes!.name!.EscapeMarkup()}[/]");
        MarkupLine($"State: {project.attributes.state.EscapeMarkup()}");

        var tags = project?.attributes?.projecttaglist ?? new string[0];
        if (tags.Any())
        {
            MarkupLine($"Tags: {string.Join(',', tags).EscapeMarkup()}");
        }

        WriteLine($"URL: {project!.links!.self}");
        WriteLine();
    }
}

void PrintChangedProjects(List<ChangedProject> changedProjects)
{
    if (changedProjects.Count == 0) return;

    WriteLine();
    MarkupLine("[bold underline green]Changed Projects[/]");
    WriteLine();

    foreach (var changedProject in changedProjects)
    {
        MarkupLine($"[bold underline]{changedProject.LatestVersion.attributes!.name!.EscapeMarkup()}[/]");

        foreach (var change in changedProject.Changes)
        {
            WriteLine($"{change.Key}: '{change.Value.OldValue}' -> '{change.Value.NewValue}'");
        }

        WriteLine();
    }
}

record ChangedProject(Project OldVersion, Project LatestVersion, Dictionary<string, AttributeChange> Changes);
