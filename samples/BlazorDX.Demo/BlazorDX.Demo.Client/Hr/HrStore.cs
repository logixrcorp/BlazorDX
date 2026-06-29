namespace BlazorDX.Demo.Client.Hr;

/// <summary>
/// In-memory HRIS data: the employee roster (with a reporting hierarchy), time-off requests, and
/// onboarding state. Registered <b>Scoped</b> (never Singleton — the BlazorDX analyzer enforces
/// that) so each session gets its own isolated company.
/// </summary>
public sealed class HrStore
{
    private readonly List<Employee> _employees = new();
    private readonly List<LeaveRequest> _leave = new();
    private int _nextLeaveId = 1;

    public HrStore()
    {
        SeedEmployees();
        SeedLeave();
    }

    // ---- employee queries --------------------------------------------------

    public IReadOnlyList<Employee> All() => _employees;

    public Employee? Get(int id) => _employees.FirstOrDefault(e => e.Id == id);

    public string ManagerName(Employee e) => e.ManagerId is int m ? Get(m)?.Name ?? "—" : "—";

    public IReadOnlyList<EmployeeRow> DirectoryRows() =>
        _employees.Select(e => e.ToRow(ManagerName(e))).ToList();

    public int Headcount => _employees.Count(e => e.Status != EmployeeStatus.Terminated);

    public int OnLeaveCount => _employees.Count(e => e.Status == EmployeeStatus.OnLeave);

    public int OnboardingCount => _employees.Count(e => e.Status == EmployeeStatus.Onboarding);

    public int PendingLeaveCount => _leave.Count(l => l.Status == LeaveStatus.Pending);

    public double AverageTenureYears =>
        _employees.Count == 0 ? 0 : _employees.Average(e => e.TenureYears);

    /// <summary>Active headcount per department, for the dashboard bar chart.</summary>
    public IReadOnlyList<(string Label, int Count)> HeadcountByDepartment() =>
        _employees.Where(e => e.Status != EmployeeStatus.Terminated)
            .GroupBy(e => e.Department)
            .OrderByDescending(g => g.Count())
            .Select(g => (g.Key.ToString(), g.Count()))
            .ToList();

    /// <summary>Headcount per employment type, for the dashboard donut.</summary>
    public IReadOnlyList<(string Label, int Count)> HeadcountByType() =>
        _employees.Where(e => e.Status != EmployeeStatus.Terminated)
            .GroupBy(e => e.EmploymentType)
            .Select(g => (HrUi.TypeLabel(g.Key), g.Count()))
            .ToList();

    /// <summary>Hires in each of the last six months (oldest first), for the trend line.</summary>
    public IReadOnlyList<(string Label, int Count)> HiresLastSixMonths()
    {
        List<(string, int)> result = new();
        DateTime now = DateTime.Today;
        for (int i = 5; i >= 0; i--)
        {
            DateTime month = new(now.Year, now.Month, 1);
            month = month.AddMonths(-i);
            int count = _employees.Count(e =>
                e.HireDate.Year == month.Year && e.HireDate.Month == month.Month);
            result.Add((month.ToString("MMM"), count));
        }

        return result;
    }

    public void UpdateProfile(Employee e, EmployeeProfile p)
    {
        e.Name = p.Name.Trim();
        e.Title = p.Title.Trim();
        e.Department = p.Department;
        e.EmploymentType = p.EmploymentType;
        e.Email = p.Email.Trim();
        e.Location = p.Location.Trim();
    }

    public static EmployeeProfile ProfileOf(Employee e) => new()
    {
        Name = e.Name,
        Title = e.Title,
        Department = e.Department,
        EmploymentType = e.EmploymentType,
        Email = e.Email,
        Location = e.Location,
    };

    // ---- org tree ----------------------------------------------------------

    /// <summary>Builds the reporting tree: roots are employees with no manager.</summary>
    public IReadOnlyList<OrgNode> OrgRoots()
    {
        Dictionary<int, OrgNode> nodes = _employees.ToDictionary(e => e.Id, e => new OrgNode
        {
            Name = e.Name,
            Title = e.Title,
            Department = e.Department.ToString(),
        });

        List<OrgNode> roots = new();
        foreach (Employee e in _employees)
        {
            if (e.ManagerId is int m && nodes.TryGetValue(m, out OrgNode? parent))
            {
                parent.Children.Add(nodes[e.Id]);
            }
            else
            {
                roots.Add(nodes[e.Id]);
            }
        }

        foreach (OrgNode n in nodes.Values)
        {
            n.Reports = n.Children.Count;
        }

        return roots;
    }

    // ---- time off ----------------------------------------------------------

    public IReadOnlyList<LeaveRequest> AllLeave() =>
        _leave.OrderByDescending(l => l.Status == LeaveStatus.Pending).ThenBy(l => l.Start).ToList();

    /// <summary>Dates inside any approved leave window — for the calendar's marker dots.</summary>
    public IReadOnlyCollection<DateOnly> ApprovedLeaveDates()
    {
        HashSet<DateOnly> dates = new();
        foreach (LeaveRequest l in _leave.Where(l => l.Status == LeaveStatus.Approved))
        {
            for (DateOnly d = l.Start; d <= l.End; d = d.AddDays(1))
            {
                dates.Add(d);
            }
        }

        return dates;
    }

    public IReadOnlyList<LeaveRequest> LeaveOn(DateOnly day) =>
        _leave.Where(l => l.Status == LeaveStatus.Approved && l.Start <= day && day <= l.End).ToList();

    public void Decide(LeaveRequest l, LeaveStatus decision)
    {
        l.Status = decision;
        Employee? e = Get(l.EmployeeId);
        if (e is not null)
        {
            e.Status = decision == LeaveStatus.Approved ? EmployeeStatus.OnLeave : EmployeeStatus.Active;
        }
    }

    // ---- onboarding --------------------------------------------------------

    /// <summary>New hires grouped by onboarding stage (for the kanban board).</summary>
    public IReadOnlyList<(OnboardingStage Stage, IReadOnlyList<Employee> People)> OnboardingByStage()
    {
        OnboardingStage[] stages =
            [OnboardingStage.Offer, OnboardingStage.Paperwork, OnboardingStage.Equipment, OnboardingStage.Orientation];
        return stages
            .Select(s => (s, (IReadOnlyList<Employee>)_employees
                .Where(e => e.Status == EmployeeStatus.Onboarding && e.OnboardingStage == s).ToList()))
            .ToList();
    }

    // ---- seed --------------------------------------------------------------

    private void Add(int id, string name, string title, Department dept, int? mgr, EmploymentType type,
        EmployeeStatus status, string location, int hireYearsAgo, int hireMonth, int salary,
        OnboardingStage stage = OnboardingStage.Complete)
    {
        _employees.Add(new Employee
        {
            Id = id,
            Name = name,
            Title = title,
            Department = dept,
            Email = $"{name.Split(' ')[0].ToLowerInvariant()}@contoso.com",
            ManagerId = mgr,
            EmploymentType = type,
            Status = status,
            Location = location,
            HireDate = new DateOnly(DateTime.Today.Year - hireYearsAgo, hireMonth, 12),
            Salary = salary,
            PtoBalanceDays = 8 + (id % 12),
            OnboardingStage = stage,
        });
    }

    private void SeedEmployees()
    {
        Add(1, "Maya Chen", "Chief Executive Officer", Department.People, null, EmploymentType.FullTime, EmployeeStatus.Active, "New York", 9, 3, 320_000);
        Add(2, "Raj Patel", "VP Engineering", Department.Engineering, 1, EmploymentType.FullTime, EmployeeStatus.Active, "Remote", 7, 6, 245_000);
        Add(3, "Elena Sokolova", "VP Product", Department.Product, 1, EmploymentType.FullTime, EmployeeStatus.Active, "Berlin", 6, 1, 240_000);
        Add(4, "Marcus Lee", "VP Sales", Department.Sales, 1, EmploymentType.FullTime, EmployeeStatus.Active, "Austin", 5, 9, 230_000);
        Add(5, "Dana Ortiz", "VP People", Department.People, 1, EmploymentType.FullTime, EmployeeStatus.Active, "New York", 5, 2, 225_000);
        Add(6, "Sam Reyes", "Engineering Manager", Department.Engineering, 2, EmploymentType.FullTime, EmployeeStatus.Active, "Remote", 4, 4, 195_000);
        Add(7, "Priya Nair", "Senior Engineer", Department.Engineering, 6, EmploymentType.FullTime, EmployeeStatus.Active, "Toronto", 3, 7, 168_000);
        Add(8, "Tom Becker", "Engineer", Department.Engineering, 6, EmploymentType.FullTime, EmployeeStatus.OnLeave, "Remote", 2, 5, 142_000);
        Add(9, "Aki Tanaka", "Engineering Intern", Department.Engineering, 6, EmploymentType.Intern, EmployeeStatus.Active, "Remote", 0, 6, 64_000);
        Add(10, "Lena Vogt", "Engineer", Department.Engineering, 6, EmploymentType.FullTime, EmployeeStatus.Active, "Berlin", 1, 11, 138_000);
        Add(11, "Jordan Kim", "Product Manager", Department.Product, 3, EmploymentType.FullTime, EmployeeStatus.Active, "Remote", 3, 2, 162_000);
        Add(12, "Noah Park", "Product Manager", Department.Product, 3, EmploymentType.FullTime, EmployeeStatus.Onboarding, "Seattle", 0, DateTime.Today.Month, 158_000, OnboardingStage.Paperwork);
        Add(13, "Iris Vance", "Design Lead", Department.Design, 3, EmploymentType.FullTime, EmployeeStatus.Active, "London", 4, 8, 172_000);
        Add(14, "Leo Marsh", "Product Designer", Department.Design, 13, EmploymentType.Contract, EmployeeStatus.Active, "Remote", 1, 3, 120_000);
        Add(15, "Sofia Rossi", "Account Executive", Department.Sales, 4, EmploymentType.FullTime, EmployeeStatus.Active, "Austin", 2, 10, 130_000);
        Add(16, "Diego Alvarez", "Account Executive", Department.Sales, 4, EmploymentType.FullTime, EmployeeStatus.Onboarding, "Remote", 0, DateTime.Today.Month, 128_000, OnboardingStage.Equipment);
        Add(17, "Hana Suzuki", "Marketing Manager", Department.Marketing, 4, EmploymentType.FullTime, EmployeeStatus.Active, "Tokyo", 3, 5, 148_000);
        Add(18, "Lee Chan", "Controller", Department.Finance, 1, EmploymentType.FullTime, EmployeeStatus.Active, "New York", 6, 4, 175_000);
        Add(19, "Grace Owens", "Recruiter", Department.People, 5, EmploymentType.FullTime, EmployeeStatus.Active, "Remote", 2, 1, 112_000);
        Add(20, "Omar Said", "HR Business Partner", Department.People, 5, EmploymentType.FullTime, EmployeeStatus.Onboarding, "Remote", 0, DateTime.Today.Month, 134_000, OnboardingStage.Orientation);
    }

    private void AddLeave(int empId, LeaveType type, int startInDays, int days, LeaveStatus status, string reason)
    {
        Employee? e = Get(empId);
        DateOnly start = DateOnly.FromDateTime(DateTime.Today.AddDays(startInDays));
        _leave.Add(new LeaveRequest
        {
            Id = _nextLeaveId++,
            EmployeeId = empId,
            EmployeeName = e?.Name ?? "Unknown",
            Type = type,
            Start = start,
            End = start.AddDays(days - 1),
            Status = status,
            Reason = reason,
        });
    }

    private void SeedLeave()
    {
        AddLeave(8, LeaveType.Parental, -3, 20, LeaveStatus.Approved, "Parental leave");
        AddLeave(7, LeaveType.Vacation, 9, 5, LeaveStatus.Pending, "Family trip");
        AddLeave(15, LeaveType.Vacation, 14, 3, LeaveStatus.Pending, "Long weekend");
        AddLeave(11, LeaveType.Sick, 2, 2, LeaveStatus.Approved, "Flu");
        AddLeave(10, LeaveType.Personal, 21, 1, LeaveStatus.Pending, "Appointment");
        AddLeave(17, LeaveType.Vacation, 5, 4, LeaveStatus.Approved, "Conference + PTO");
    }
}
