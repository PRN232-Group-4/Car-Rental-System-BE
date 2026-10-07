

namespace Car_Rental_System_BE.Application.Interfaces.Repositories;

public sealed class StaffNotFoundException : Exception;
public sealed class OrganizationAssignmentForbiddenException : Exception;
public sealed class StaffConflictException(string message) : Exception(message);
public sealed class OrganizationReferenceNotFoundException(string message) : Exception(message);
