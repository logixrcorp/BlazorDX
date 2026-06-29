using System.ComponentModel.DataAnnotations;
using BlazorDX.Primitives.Forms;
using BlazorDX.Primitives.Grid;

namespace BlazorDX.Demo.Client.Hr;

public enum Department { Engineering, Product, Design, Sales, Marketing, Finance, People, Support }

public enum EmploymentType { FullTime, PartTime, Contract, Intern }

public enum EmployeeStatus { Active, OnLeave, Onboarding, Terminated }

public enum LeaveType { Vacation, Sick, Personal, Parental }

public enum LeaveStatus { Pending, Approved, Denied }

public enum OnboardingStage { Offer, Paperwork, Equipment, Orientation, Complete }

/// <summary>A person in the HRIS. The directory grid sees a flat <see cref="EmployeeRow"/>; the
/// profile editor edits an <see cref="EmployeeProfile"/>. Manager is by id, so the org tree builds.</summary>
public sealed class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Department Department { get; set; }
    public string Email { get; set; } = string.Empty;
    public int? ManagerId { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    public string Location { get; set; } = "Remote";
    public DateOnly HireDate { get; set; }
    public int Salary { get; set; }
    public int PtoBalanceDays { get; set; } = 15;
    public OnboardingStage OnboardingStage { get; set; } = OnboardingStage.Complete;

    public string Initials
    {
        get
        {
            string[] p = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return p.Length == 0 ? "?" : p.Length == 1
                ? char.ToUpperInvariant(p[0][0]).ToString()
                : $"{char.ToUpperInvariant(p[0][0])}{char.ToUpperInvariant(p[^1][0])}";
        }
    }

    public double TenureYears => (DateTime.Today - HireDate.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.25;

    public EmployeeRow ToRow(string manager) => new()
    {
        Id = Id,
        Name = Name,
        Title = Title,
        Department = Department.ToString(),
        Type = HrUi.TypeLabel(EmploymentType),
        Status = HrUi.StatusLabel(Status),
        Location = Location,
        Manager = manager,
        Hired = HireDate.ToString("MMM yyyy"),
    };
}

/// <summary>Flat directory-grid projection (string/int — drives BlazorDX.SourceGen).</summary>
[GridRow]
public sealed class EmployeeRow
{
    [GridColumn("Name", Order = 0)] public string Name { get; set; } = string.Empty;
    [GridColumn("Title", Order = 1)] public string Title { get; set; } = string.Empty;
    [GridColumn("Department", Order = 2)] public string Department { get; set; } = string.Empty;
    [GridColumn("Type", Order = 3)] public string Type { get; set; } = string.Empty;
    [GridColumn("Status", Order = 4)] public string Status { get; set; } = string.Empty;
    [GridColumn("Location", Order = 5)] public string Location { get; set; } = string.Empty;
    [GridColumn("Manager", Order = 6)] public string Manager { get; set; } = string.Empty;
    [GridColumn("Hired", Order = 7)] public string Hired { get; set; } = string.Empty;
    public int Id { get; set; }
}

/// <summary>The editable subset of an employee, rendered + validated by <c>DxForm</c>.</summary>
[DxFormModel(Name = "employee_profile", Description = "Edit an employee's core HR profile.")]
public sealed class EmployeeProfile
{
    [Required, StringLength(80, MinimumLength = 2)]
    [Display(Name = "Full name", Order = 0)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(80, MinimumLength = 2)]
    [Display(Name = "Title", Order = 1)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Department", Order = 2)]
    public Department Department { get; set; }

    [Display(Name = "Employment type", Order = 3)]
    public EmploymentType EmploymentType { get; set; }

    [Required, EmailAddress]
    [Display(Name = "Work email", Order = 4)]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Location", Order = 5)]
    public string Location { get; set; } = string.Empty;
}

/// <summary>A time-off request flowing through the approval workflow.</summary>
public sealed class LeaveRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public LeaveType Type { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public string Reason { get; set; } = string.Empty;

    public int Days => End.DayNumber - Start.DayNumber + 1;
}

/// <summary>An org-chart node — both a grid row (columns) and a tree node (children),
/// the same dual shape the TreeGrid demo uses.</summary>
[GridRow]
public sealed class OrgNode
{
    [GridColumn("Name", Order = 0)] public string Name { get; set; } = string.Empty;
    [GridColumn("Title", Order = 1)] public string Title { get; set; } = string.Empty;
    [GridColumn("Department", Order = 2)] public string Department { get; set; } = string.Empty;
    [GridColumn("Direct reports", Order = 3)] public int Reports { get; set; }
    public List<OrgNode> Children { get; } = new();
}

/// <summary>Shared label/colour helpers for the HRIS demo.</summary>
public static class HrUi
{
    public static string TypeLabel(EmploymentType t) => t switch
    {
        EmploymentType.FullTime => "Full-time",
        EmploymentType.PartTime => "Part-time",
        _ => t.ToString(),
    };

    public static string StatusLabel(EmployeeStatus s) => s switch
    {
        EmployeeStatus.OnLeave => "On leave",
        _ => s.ToString(),
    };

    public static string StatusVariant(EmployeeStatus s) => s switch
    {
        EmployeeStatus.Active => "success",
        EmployeeStatus.OnLeave => "warning",
        EmployeeStatus.Onboarding => "info",
        EmployeeStatus.Terminated => "danger",
        _ => "default",
    };

    public static string LeaveVariant(LeaveStatus s) => s switch
    {
        LeaveStatus.Approved => "success",
        LeaveStatus.Denied => "danger",
        _ => "warning",
    };
}
