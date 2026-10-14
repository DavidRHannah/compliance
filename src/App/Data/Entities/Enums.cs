namespace App.Data.Entities;

// Each enum is a PostgreSQL enum type. Npgsql stores the names in snake_case,
// so PreliminaryPass is stored as preliminary_pass.

public enum UserRole { Consultant, Admin, Client }

public enum EngagementStatus { Draft, Active, Closed }

public enum ScopeItemType { System, Asset, Vendor }

public enum DocumentStatus { Pending, Processing, Ready, Failed }

public enum MappingRelationship { Equivalent, Subset, Superset, Related }

public enum MentionMethod { Alias, Llm }

public enum RunStatus { Queued, Running, Completed, Failed }

public enum PreliminaryVerdict { PreliminaryPass, PreliminaryFail, InterpretationRequired, InsufficientEvidence }

public enum FinalVerdict { Pass, Fail, InsufficientEvidence }

public enum ReviewFlagReason { DecisionSuperseded, DocumentDeleted, DocumentReplaced }
