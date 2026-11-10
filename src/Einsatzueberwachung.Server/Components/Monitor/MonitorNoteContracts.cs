namespace Einsatzueberwachung.Server.Components.Monitor;

public sealed record MonitorNoteSubmission(string Text, string SourceId, string NoteType);

public sealed record MonitorQuickNoteSubmission(string Text, string SourceId);

public sealed record MonitorReplySubmission(string NoteId, string Text, string SourceId);
