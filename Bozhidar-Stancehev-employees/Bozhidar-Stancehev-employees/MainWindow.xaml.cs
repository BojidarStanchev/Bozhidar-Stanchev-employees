using System.Windows;
using System.Windows.Documents;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Win32;
using CsvHelper;
using System.IO;
using CsvHelper.Configuration.Attributes;
using System.Globalization;
using System.Linq;
using System;
using System.Windows.Shapes;

namespace Bozhidar_Stancehev_employees;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
	}

	/// <summary>
	/// Open dialog to choose file.
	/// </summary>
	private void ButtonChooseFile_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog();
		dialog.Filter = "CSV documents (.csv)|*.csv";

		if(dialog.ShowDialog() == true)
		{
			using (StreamReader reader = new StreamReader(dialog.FileName))
			using (CsvReader csv = new CsvReader(reader, CultureInfo.InvariantCulture))
			{
				List<InputDTO> input = csv.GetRecords<InputDTO>().ToList(); //TODO: not like this
				GuessCulture(input);
			}
		}
	}

	/// <summary>
	/// Since it's not clear what "supports all cultures" means, I'll assume you don't want a dropdown to choose a date format.
	/// Instead we should try to guess the correct format.
	/// Uses the first culture that can parse all from dates.
	/// This assumes the file contains dates with consistent culture.
	/// </summary>
	/// <param name="input"></param>
	private void GuessCulture(List<InputDTO> input)
	{
		List<CultureInfo> ShortDateCultures = CultureInfo.GetCultures(CultureTypes.AllCultures)
			.GroupBy(c => c.DateTimeFormat.ShortDatePattern)
			.Select(g => g.First())
			.ToList();

		List<CultureInfo> LongDateCultures = CultureInfo.GetCultures(CultureTypes.AllCultures)
			.GroupBy(c => c.DateTimeFormat.LongDatePattern) 
			.Select(g => g.First())
			.ToList();

		foreach (CultureInfo shortPattern in ShortDateCultures)
		{
			if(TestInputOnCulture(input, shortPattern))
			{
				ParseDates(input, shortPattern);
				return;
			}
		}

		foreach(CultureInfo longPattern in LongDateCultures)
		{
			if(TestInputOnCulture(input, longPattern)) 
			{ 
				ParseDates(input, longPattern);
				return;
			}
		}
	}

	/// <summary>
	/// Validate if all from dates from input can be parsed. 
	/// </summary>
	/// <param name="input"></param>
	/// <param name="culture"></param>
	/// <returns></returns>
	bool TestInputOnCulture(List<InputDTO> input, CultureInfo culture)
	{
		foreach (var line in input)
		{
			if (!DateTime.TryParse(line.DateFrom, culture, DateTimeStyles.None, out _))
			{
				return false;
			}

			if(line.DateTo.Trim() != "NULL" && !DateTime.TryParse(line.DateTo, culture, DateTimeStyles.None, out _))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Parse and save dates along with the rest of the data. 
	/// </summary>
	/// <param name="input"></param>
	/// <param name="cultureInfo"></param>
	private void ParseDates(List<InputDTO> input, CultureInfo cultureInfo)
	{
		List<EmployeeData> data = new List<EmployeeData>();
		foreach (var line in input)
		{
			data.Add(new EmployeeData()
			{
				EmployeeID = line.EmployeeID,
				ProjectID = line.ProjectID,
				DateFrom = DateTime.Parse(line.DateFrom, cultureInfo),
				DateTo = line.DateTo == "NULL" ? DateTime.Now.Date : DateTime.Parse(line.DateTo, cultureInfo)
			});
		}

		AnalyseData(data);
	}

	/// <summary>
	/// List all collaborations between the two employees that have the most work time shared between projects.
	/// </summary>
	/// <param name="data"></param>
	private void AnalyseData(List<EmployeeData> data)
	{
		List<ResultDTO> collaborations = FindAllCollaborations(data);

		(string employeeID1, string employeeID2) = FindMostCollaborationDays(collaborations);

		var filteredCollaborations = collaborations
			.Where(c => (c.EmployeeID1 == employeeID1 && c.EmployeeID2 == employeeID2) || 
						(c.EmployeeID1 == employeeID2 && c.EmployeeID2 == employeeID1))
			.ToList();

		//update the datagrid with the content
		DataGrid.ItemsSource = filteredCollaborations;
	}

	/// <summary>
	/// Find out which pair of employees has the highest count of days worked on common projects.
	/// </summary>
	/// <param name="collaborations"></param>
	/// <returns></returns>
	private (string ID1, string ID2) FindMostCollaborationDays(List<ResultDTO> collaborations)
	{
		Dictionary<(string, string), int> scores = new Dictionary<(string, string), int>();

		foreach(ResultDTO item in collaborations)
		{
			if(scores.ContainsKey((item.EmployeeID1, item.EmployeeID2)))
			{
				scores[(item.EmployeeID1, item.EmployeeID2)] += item.DaysWorked;
			}
			else if(scores.ContainsKey((item.EmployeeID2, item.EmployeeID1)))
			{
				scores[(item.EmployeeID2, item.EmployeeID1)] += item.DaysWorked;
			}
			else
			{
				scores.Add((item.EmployeeID1, item.EmployeeID2), item.DaysWorked);
			}
		}

		return scores.MaxBy(kvp => kvp.Value).Key;
	}

	/// <summary>
	/// Simply find all collaborations between people on all projects.
	/// </summary>
	/// <param name="data"></param>
	/// <returns></returns>
	private List<ResultDTO> FindAllCollaborations(List<EmployeeData> data)
	{
		var projectGroups = data.GroupBy(g => g.ProjectID);

		int longestCollaborationInDays = 0;
		string employeeID1;
		string employeeID2;

		//Dictionary<(string, string), List<(string, int)>> collaborationInfo = new Dictionary<(string, string), List<(string, int)>>();
		List<ResultDTO> collaborationInfo = new List<ResultDTO>();

		foreach (var projectGroup in projectGroups)
		{
			var employeesInProject = projectGroup.Select(e => e.EmployeeID).Distinct().ToList();

			for (int i = 0; i < employeesInProject.Count - 1; i++)
			{
				for (int j = i + 1; j < employeesInProject.Count; j++)
				{
					int collaborationDuration = CalculateCollaborationDuration(projectGroup, employeesInProject[i], employeesInProject[j]);

					//save id1, id2, projectid, days
					ResultDTO entry = new ResultDTO()
					{
						EmployeeID1 = employeesInProject[i],
						EmployeeID2 = employeesInProject[j],
						ProjectID = projectGroup.First().ProjectID,
						DaysWorked = collaborationDuration
					};
					
					collaborationInfo.Add(entry);

					if (longestCollaborationInDays < collaborationDuration)
					{
						longestCollaborationInDays = collaborationDuration;
						employeeID1 = employeesInProject[i];
						employeeID2 = employeesInProject[j];
					}
				}
			}
		}

		return collaborationInfo.Where(ci => ci.DaysWorked > 0).ToList();
	}

	/// <summary>
	/// Calculate collaboration duration in days for a given project.
	/// </summary>
	/// <param name="projectData"></param>
	/// <param name="employeeID1"></param>
	/// <param name="employeeID2"></param>
	/// <returns></returns>
	private int CalculateCollaborationDuration(IEnumerable<EmployeeData> projectData, string employeeID1, string employeeID2)
	{
		IEnumerable<(DateTime DateFrom, DateTime DateTo)> datesEmployee1 = projectData.Where(e => e.EmployeeID == employeeID1).Select(e => (e.DateFrom, e.DateTo));
		IEnumerable<(DateTime DateFrom, DateTime DateTo)> datesEmployee2 = projectData.Where(e => e.EmployeeID == employeeID2).Select(e => (e.DateFrom, e.DateTo));

		var commonDates = datesEmployee1.Join(datesEmployee2,
			date1 => date1,
			date2 => date2,
			(date1, date2) => (date1.Item1 > date2.Item2 || date2.Item1 > date1.Item2) ? TimeSpan.Zero :
				(date1.Item2 < date2.Item2 ? date1.Item2 : date2.Item2) - (date1.Item1 > date2.Item1 ? date1.Item1 : date2.Item1));

		return commonDates.Any() ? commonDates.Max().Days : 0;
	}

	/// <summary>
	/// Simple DTO used by the CSV reader.
	/// Annotations should match csv header keys.
	/// </summary>
	public class InputDTO
	{
		[Name("EmpID")]
		public string EmployeeID { get; set; }
		[Name("ProjectID")]
		public string ProjectID { get; set; }
		[Name("DateFrom")]
		public string DateFrom { get; set; }
		[Name("DateTo")]
		public string DateTo { get; set; }
	}

	/// <summary>
	/// Object used after I figure out the culture of the dates and before we calculate the final results.
	/// </summary>
	public class EmployeeData
	{
		public string EmployeeID { get; set; }
		public string ProjectID { get; set; }
		public DateTime DateFrom { get; set; }
		public DateTime DateTo { get; set; }
	}

	/// <summary>
	/// DTO used to pass data to the DataGrid so I can display the final result in the UI.
	/// </summary>
	public class ResultDTO
	{
		public string EmployeeID1 { get; set; }
		public string EmployeeID2 { get; set; }
		public string ProjectID { get; set; }
		public int DaysWorked { get; set; }
	}
}

