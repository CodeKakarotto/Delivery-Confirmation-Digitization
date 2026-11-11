namespace Confirmations.Api.Data.Entities;

// All enums are stored as readable text in SQL Server (e.g. "Approved").

/// <summary>Where a delivery confirmation record is in the review process.</summary>
public enum ConfirmationStatus
{
    /// <summary>Waiting for an employee to check it.</summary>
    Pending,
    /// <summary>Checked and approved by a person: this is the official confirmation.</summary>
    Approved,
    /// <summary>Checked and rejected (e.g. unreadable, wrong sheet).</summary>
    Rejected,
    /// <summary>Sent to a manager to decide.</summary>
    Flagged,
    /// <summary>A manager cancelled a previously approved confirmation, with a reason.</summary>
    Voided,
}

/// <summary>Kinds of problems the system points out for review. Never automatic decisions.</summary>
public enum FlagType
{
    MissingField,
    OrderNotFound,
    OrderAlreadyConfirmed,
    /// <summary>Another sheet for the same order is also waiting for review.</summary>
    DuplicateOrder,
    DuplicateFile,
    InvalidTotalProducts,
    UnknownZone,
    MissingSignature,
    LowConfidence,
    OcrFailed,
}

/// <summary>Actions recorded in the audit log.</summary>
public enum AuditAction
{
    Uploaded,
    Edited,
    Approved,
    Rejected,
    SentToManager,
    Resolved,
    Voided,
}
