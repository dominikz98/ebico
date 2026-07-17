using EBICO.Connector.Quickstart;

// Minimaler EBICS-Quickstart: startet den EBICO.Server-Emulator in-process und fährt mit dem
// EBICO.Connector den vollständigen Rundlauf Onboarding -> Upload -> Download. Kein externer
// Server, keine echte Bank nötig — einfach `dotnet run`.
var result = await QuickstartRunner.RunAsync(Console.Out);

// Exit-Code 0 nur, wenn jeder Schritt fachlich erfolgreich war (für CI/Skripte).
return result.Success ? 0 : 1;
