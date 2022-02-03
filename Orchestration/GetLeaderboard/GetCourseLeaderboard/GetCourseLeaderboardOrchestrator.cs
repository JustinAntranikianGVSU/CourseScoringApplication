﻿using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace Orchestration.GetLeaderboard.GetCourseLeaderboard;

public class GetCourseLeaderboardOrchestrator
{
	private readonly ScoringDbContext _scoringDbContext;

	public GetCourseLeaderboardOrchestrator(ScoringDbContext scoringDbContext)
	{
		_scoringDbContext = scoringDbContext;
	}

	public async Task<CourseLeaderboardDto> GetCourseLeaderboardDto(int courseId, int? bracketId, int? intervalId, int startingRank = 1, int take = 50)
	{
		var course = await _scoringDbContext.Courses.Include(oo => oo.Brackets).Include(oo => oo.Intervals).SingleAsync(oo => oo.Id == courseId);
		var allCourses = await _scoringDbContext.Courses.Where(oo => oo.RaceId == course.RaceId).ToListAsync();

		var race = await _scoringDbContext.Races.Include(oo => oo.RaceSeries).Include(oo => oo.Courses).SingleAsync(oo => oo.Id == course.RaceId);
		var bracketToUse = bracketId.HasValue ? course.Brackets.Single(oo => oo.Id == bracketId) : course.Brackets.Single(oo => oo.BracketType == BracketType.Overall);

		var results = await GetResults(bracketToUse.Id, intervalId, startingRank, take);
		var metadataEntries = await GetMetadataEntries(bracketToUse.Id, intervalId);
		var courseMetadata = GetCourseMetadata(allCourses, course);
		var courseResultDtos = GetCourseResultByIntervalDtos(results, metadataEntries, course, bracketToUse.Id).ToList();

		return CourseLeaderboardDtoMapper.GetCourseLeaderboardDto(course, race, courseMetadata, courseResultDtos);
	}

	private async Task<List<Result>> GetResults(int bracketId, int? intervalId, int startingRank, int take)
	{
		var endingRank = startingRank + (take - 1);
		var wantsHighestIntervalCompleted = !intervalId.HasValue;

		var query = _scoringDbContext.Results
						.Include(oo => oo.AthleteCourse)
						.ThenInclude(oo => oo.Athlete)
						.Where(oo =>
							oo.BracketId == bracketId &&
							oo.IsHighestIntervalCompleted == wantsHighestIntervalCompleted &&
							oo.Rank >= startingRank &&
							oo.Rank <= endingRank
						);

		if (intervalId.HasValue)
		{
			query = query.Where(oo => oo.IntervalId == intervalId);
		}

		return await query.OrderBy(oo => oo.Rank).ToListAsync();
	}

	private async Task<List<BracketMetadata>> GetMetadataEntries(int bracketId, int? intervalId)
	{
		var query = _scoringDbContext.BracketMetadataEntries.Where(oo => oo.BracketId == bracketId);

		if (intervalId is int)
		{
			query = query.Where(oo => oo.IntervalId == intervalId);
		}
		else
		{
			// just get metadata for all intervals. There might be some extra intervals retrieved, but that amount is negligible.
			query = query.Where(oo => oo.IntervalId != null);
		}

		return await query.ToListAsync();
	}

	private static CourseMetadata GetCourseMetadata(List<Course> courses, Course course)
	{
		var courseDtos = courses.Select(oo => new DisplayNameWithIdDto(oo.Id, oo.Name)).ToList();
		var intervalDtos = course.Intervals.OrderBy(oo => oo.Order).Select(oo => new DisplayNameWithIdDto(oo.Id, oo.Name)).ToList();
		var bracketDtos = course.Brackets
							.Select(oo => new BracketMetaData(oo.Id, oo.Name, oo.BracketType))
							.OrderBy(oo => oo.BracketType)
							.ThenBy(oo => oo.DisplayName)
							.ToList();

		return new CourseMetadata(courseDtos, bracketDtos, intervalDtos);
	}

	private IEnumerable<CourseLeaderboardByIntervalDto> GetCourseResultByIntervalDtos(List<Result> results, List<BracketMetadata> metadata, Course course, int bracketId)
	{
		foreach (var resultsByInterval in results.GroupBy(oo => oo.IntervalId))
		{
			var interval = course.Intervals.Single(oo => oo.Id == resultsByInterval.Key);
			var bracketMeta = metadata.Single(oo => oo.BracketId == bracketId && oo.IntervalId == resultsByInterval.Key);
			var resultDtos = resultsByInterval.Select(oo => GetResultByCourseDto(oo, course, interval)).ToList();
			yield return new CourseLeaderboardByIntervalDto(interval.Name, interval.IntervalType, bracketMeta.TotalRacers, resultDtos);
		}
	}

	private static LeaderboardResultDto GetResultByCourseDto(Result result, Course course, Interval interval)
	{
		var timeOnCoursePace = course.GetPaceWithTime(result.TimeOnCourse, interval.DistanceFromStart);
		var athlete = result.AthleteCourse.Athlete;
		return LeaderboardResultDtoMapper.GetLeaderboardResultDto(result, athlete, timeOnCoursePace, course);
	}
}
