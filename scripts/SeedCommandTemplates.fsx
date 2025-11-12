#!/usr/bin/env dotnet fsi

// Script to seed HyperDeck command templates into the database
// Usage: dotnet fsi SeedCommandTemplates.fsx [connection-string]

open System
open System.Data.SqlClient

let connectionString = 
    match fsi.CommandLineArgs with
    | [| _; connStr |] -> connStr
    | _ -> 
        printfn "Usage: dotnet fsi SeedCommandTemplates.fsx <connection-string>"
        printfn "Example: dotnet fsi SeedCommandTemplates.fsx \"Server=localhost;Database=ProdControlAV;Trusted_Connection=True;\""
        Environment.Exit(1)
        ""

printfn "Connecting to database..."
printfn "Connection string: %s" connectionString

// Note: This is a placeholder script
// The actual seeding can be done programmatically in C# using the CommandTemplateSeeder class
// Or through EF Core migrations with HasData

printfn ""
printfn "To seed the database, use the CommandTemplateSeeder class in your application startup"
printfn "or create a migration with HasData configuration."
