using System;
using System.Collections.Generic;
using System.Linq;
using MortgageLoanAPI.Data;
using MortgageLoanAPI.DTOs;
using MortgageLoanAPI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace MortgageLoanAPI.Services;

/// <summary>
/// Interface for callback request service
/// </summary>
public interface ICallbackRequestService
{
    Task<CallbackRequest> CreateCallbackRequestAsync(CallbackRequestDto request);
    Task<List<CallbackRequest>> GetAllCallbackRequestsAsync();
    Task<(List<CallbackRequest> Items, int Total)> GetCallbackRequestsPagedAsync(int page, int pageSize, string? q, string? statusFilter, string? dateRange);
    Task<CallbackRequest> UpdateCallbackRequestAsync(int id, DTOs.CallbackRequestUpdateDto update);
    Task BulkMarkContactedAsync(List<int> ids);
}

/// <summary>
/// Service for handling callback requests from customers
/// </summary>
public class CallbackRequestService : ICallbackRequestService
{
    private readonly MortgageDbContext _dbContext;
    private readonly ILogger<CallbackRequestService> _logger;

    public CallbackRequestService(
        MortgageDbContext dbContext,
        ILogger<CallbackRequestService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new callback request and stores in database
    /// </summary>
    public async Task<CallbackRequest> CreateCallbackRequestAsync(CallbackRequestDto request)
    {
        _logger.LogInformation($"Creating callback request for {request.FullName}");

        try
        {
            // Validate that loan result exists only when a positive ID is provided
            if (request.LoanResultId > 0)
            {
                var loanResult = await _dbContext.LoanResults.FindAsync(request.LoanResultId);
                if (loanResult == null)
                {
                    throw new InvalidOperationException($"LoanResult with ID {request.LoanResultId} not found");
                }
            }

            var callbackRequest = new CallbackRequest
            {
                LoanResultId = request.LoanResultId > 0 ? request.LoanResultId : null,
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                Email = request.Email,
                Message = request.Message,
                ReferralNumber = request.ReferralNumber,
                LoanInputsJson = request.LoanInputsJson,
                LoanResultJson = request.LoanResultJson,
                IsProcessed = false
            };

            _dbContext.CallbackRequests.Add(callbackRequest);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                $"Callback request created with ID: {callbackRequest.Id}");

            return callbackRequest;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            // Handle missing column on older DB schemas by falling back to a raw INSERT
            if (dbEx.InnerException != null && dbEx.InnerException.Message.Contains("Unknown column 'LoanInputsJson'"))
            {
                _logger.LogWarning("Database is missing callback JSON columns; falling back to raw INSERT without those columns.");
                try
                {
                    var conn = _dbContext.Database.GetDbConnection();
                    await conn.OpenAsync();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"INSERT INTO `CallbackRequests` 
    (`LoanResultId`,`FullName`,`PhoneNumber`,`Email`,`Message`,`ReferralNumber`,`IsProcessed`,`CreatedAt`) 
    VALUES (@loanResultId,@fullName,@phoneNumber,@email,@message,@referralNumber,@isProcessed,@createdAt); SELECT LAST_INSERT_ID();";

                        var p1 = cmd.CreateParameter(); p1.ParameterName = "@loanResultId"; p1.Value = request.LoanResultId; cmd.Parameters.Add(p1);
                        var p2 = cmd.CreateParameter(); p2.ParameterName = "@fullName"; p2.Value = request.FullName ?? string.Empty; cmd.Parameters.Add(p2);
                        var p3 = cmd.CreateParameter(); p3.ParameterName = "@phoneNumber"; p3.Value = request.PhoneNumber ?? string.Empty; cmd.Parameters.Add(p3);
                        var p4 = cmd.CreateParameter(); p4.ParameterName = "@email"; p4.Value = request.Email ?? string.Empty; cmd.Parameters.Add(p4);
                        var p5 = cmd.CreateParameter(); p5.ParameterName = "@message"; p5.Value = request.Message != null ? (object)request.Message : DBNull.Value; cmd.Parameters.Add(p5);
                        var p6 = cmd.CreateParameter(); p6.ParameterName = "@referralNumber"; p6.Value = request.ReferralNumber != null ? (object)request.ReferralNumber : DBNull.Value; cmd.Parameters.Add(p6);
                        var p7 = cmd.CreateParameter(); p7.ParameterName = "@isProcessed"; p7.Value = false; cmd.Parameters.Add(p7);
                        var p8 = cmd.CreateParameter(); p8.ParameterName = "@createdAt"; p8.Value = DateTime.UtcNow; cmd.Parameters.Add(p8);

                        var result = await cmd.ExecuteScalarAsync();
                        var newId = Convert.ToInt32(result);

                        var fallback = new CallbackRequest
                        {
                            Id = newId,
                            LoanResultId = request.LoanResultId,
                            FullName = request.FullName ?? string.Empty,
                            PhoneNumber = request.PhoneNumber ?? string.Empty,
                            Email = request.Email ?? string.Empty,
                            Message = request.Message,
                            ReferralNumber = request.ReferralNumber,
                            IsProcessed = false,
                            CreatedAt = DateTime.UtcNow
                        };

                        _logger.LogInformation($"Callback request created (fallback) with ID: {fallback.Id}");
                        return fallback;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Fallback raw insert failed: {ex.Message}");
                    throw;
                }
            }

            _logger.LogError($"Error creating callback request: {dbEx.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating callback request: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Returns all callback requests ordered by newest first
    /// </summary>
    public async Task<List<CallbackRequest>> GetAllCallbackRequestsAsync()
    {
        try
        {
            return await _dbContext.CallbackRequests
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fetching callback requests: {ex.Message}");

            // If the database is missing newer columns (e.g. LoanInputsJson/LoanResultJson),
            // fall back to a raw reader that selects only the core columns so the admin UI can still load.
            if (ex.Message != null && ex.Message.Contains("Unknown column"))
            {
                try
                {
                    var results = new List<CallbackRequest>();
                    var conn = _dbContext.Database.GetDbConnection();
                    await conn.OpenAsync();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"SELECT Id, LoanResultId, FullName, PhoneNumber, Email, Message, ReferralNumber, CreatedAt, IsProcessed, ProcessedAt
FROM `CallbackRequests`
ORDER BY CreatedAt DESC;";

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var cr = new CallbackRequest
                                {
                                    Id = reader.GetInt32(0),
                                    LoanResultId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                                    FullName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                    PhoneNumber = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                    Email = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                                    Message = reader.IsDBNull(5) ? null : reader.GetString(5),
                                    ReferralNumber = reader.IsDBNull(6) ? null : reader.GetString(6),
                                    CreatedAt = reader.IsDBNull(7) ? DateTime.UtcNow : reader.GetDateTime(7),
                                    IsProcessed = !reader.IsDBNull(8) && reader.GetBoolean(8),
                                    ProcessedAt = reader.IsDBNull(9) ? null : (DateTime?)reader.GetDateTime(9)
                                };
                                results.Add(cr);
                            }
                        }
                    }
                    return results;
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError($"Fallback reader failed: {fallbackEx.Message}");
                    throw;
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Returns paged callback requests with optional filters
    /// </summary>
    public async Task<(List<CallbackRequest> Items, int Total)> GetCallbackRequestsPagedAsync(int page, int pageSize, string? q, string? statusFilter, string? dateRange)
    {
        try
        {
            var query = _dbContext.CallbackRequests.AsQueryable();

            // search across common fields
            if (!string.IsNullOrWhiteSpace(q))
            {
                var qq = q.Trim().ToLower();
                query = query.Where(c => c.FullName.ToLower().Contains(qq)
                                    || c.PhoneNumber.ToLower().Contains(qq)
                                    || c.Email.ToLower().Contains(qq)
                                    || (c.ReferralNumber != null && c.ReferralNumber.ToLower().Contains(qq)));
            }

            // status filter
            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all")
            {
                if (statusFilter == "new") query = query.Where(c => !c.IsProcessed);
                if (statusFilter == "contacted") query = query.Where(c => c.IsProcessed);
                if (statusFilter == "archived") query = query.Where(c => c.IsProcessed && c.ProcessedAt != null);
            }

            // date range (simple: last N days)
            if (!string.IsNullOrWhiteSpace(dateRange) && dateRange != "all")
            {
                DateTime cutoff = DateTime.MinValue;
                if (dateRange == "1d") cutoff = DateTime.UtcNow.AddDays(-1);
                else if (dateRange == "7d") cutoff = DateTime.UtcNow.AddDays(-7);
                else if (dateRange == "30d") cutoff = DateTime.UtcNow.AddDays(-30);
                if (cutoff > DateTime.MinValue) query = query.Where(c => c.CreatedAt >= cutoff);
            }

            query = query.OrderByDescending(c => c.CreatedAt);

            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return (items, total);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fetching paged callback requests: {ex.Message}");
            throw;
        }
    }

    public async Task<CallbackRequest> UpdateCallbackRequestAsync(int id, DTOs.CallbackRequestUpdateDto update)
    {
        var cr = await _dbContext.CallbackRequests.FindAsync(id);
        if (cr == null) throw new KeyNotFoundException($"CallbackRequest {id} not found");

        if (update.IsProcessed.HasValue)
        {
            cr.IsProcessed = update.IsProcessed.Value;
            cr.ProcessedAt = update.IsProcessed.Value ? DateTime.UtcNow : (DateTime?)null;
        }
        if (update.Notes != null)
        {
            cr.Message = update.Notes;
        }

        await _dbContext.SaveChangesAsync();
        return cr;
    }

    public async Task BulkMarkContactedAsync(List<int> ids)
    {
        if (ids == null || ids.Count == 0) return;
        var items = await _dbContext.CallbackRequests.Where(c => ids.Contains(c.Id)).ToListAsync();
        foreach (var it in items)
        {
            it.IsProcessed = true;
            it.ProcessedAt = DateTime.UtcNow;
        }
        await _dbContext.SaveChangesAsync();
    }
}
