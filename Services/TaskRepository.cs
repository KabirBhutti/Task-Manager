using Microsoft.EntityFrameworkCore;
using TaskManagerAPI.Data;
using TaskManagerAPI.DTOs;
using TaskManagerAPI.Models;

namespace TaskManagerAPI.Services
{
    public interface ITaskRepository
    {
        Task<List<TaskDto>> GetAllTasksAsync();
        Task<TaskDto?> GetTaskByIdAsync(int id);
        Task<TaskDto> CreateTaskAsync(CreateTaskDto createTaskDto, int userId);
        Task<TaskDto?> UpdateTaskAsync(int id, UpdateTaskDto updateTaskDto);
        Task<bool> DeleteTaskAsync(int id);
        Task<List<TaskDto>> GetUserTasksAsync(int userId);
    }

    public class TaskRepository : ITaskRepository
    {
        private readonly AppDbContext _context;

        public TaskRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<TaskDto>> GetAllTasksAsync()
        {
            return await _context.Tasks
                .Include(t => t.User)
                .Select(t => new TaskDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    IsCompleted = t.IsCompleted,
                    DueDate = t.DueDate,
                    Priority = t.Priority,
                    CreatedAt = t.CreatedAt,
                    UserId = t.UserId,
                    Username = t.User!.Username
                })
                .ToListAsync();
        }

        public async Task<TaskDto?> GetTaskByIdAsync(int id)
        {
            var task = await _context.Tasks
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return null;

            return new TaskDto
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                IsCompleted = task.IsCompleted,
                DueDate = task.DueDate,
                Priority = task.Priority,
                CreatedAt = task.CreatedAt,
                UserId = task.UserId,
                Username = task.User!.Username
            };
        }

        public async Task<TaskDto> CreateTaskAsync(CreateTaskDto createTaskDto, int userId)
        {
            var task = new TaskItem
            {
                Title = createTaskDto.Title,
                Description = createTaskDto.Description,
                DueDate = createTaskDto.DueDate.ToUniversalTime(),
                Priority = createTaskDto.Priority,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

            var user = await _context.Users.FindAsync(userId);

            return new TaskDto
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                IsCompleted = task.IsCompleted,
                DueDate = task.DueDate,
                Priority = task.Priority,
                CreatedAt = task.CreatedAt,
                UserId = task.UserId,
                Username = user?.Username ?? "Unknown"
            };
        }

        public async Task<TaskDto?> UpdateTaskAsync(int id, UpdateTaskDto updateTaskDto)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return null;

            if (!string.IsNullOrEmpty(updateTaskDto.Title))
                task.Title = updateTaskDto.Title;

            if (!string.IsNullOrEmpty(updateTaskDto.Description))
                task.Description = updateTaskDto.Description;

            if (updateTaskDto.IsCompleted.HasValue)
                task.IsCompleted = updateTaskDto.IsCompleted.Value;

            if (updateTaskDto.DueDate.HasValue)
                task.DueDate = updateTaskDto.DueDate.Value.ToUniversalTime();

            if (!string.IsNullOrEmpty(updateTaskDto.Priority))
                task.Priority = updateTaskDto.Priority;

            await _context.SaveChangesAsync();

            var updatedTask = await _context.Tasks
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (updatedTask == null) return null;

            return new TaskDto
            {
                Id = updatedTask.Id,
                Title = updatedTask.Title,
                Description = updatedTask.Description,
                IsCompleted = updatedTask.IsCompleted,
                DueDate = updatedTask.DueDate,
                Priority = updatedTask.Priority,
                CreatedAt = updatedTask.CreatedAt,
                UserId = updatedTask.UserId,
                Username = updatedTask.User?.Username ?? "Unknown"
            };
        }

        public async Task<bool> DeleteTaskAsync(int id)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return false;

            _context.Tasks.Remove(task);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<TaskDto>> GetUserTasksAsync(int userId)
        {
            return await _context.Tasks
                .Where(t => t.UserId == userId)
                .Include(t => t.User)
                .Select(t => new TaskDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    IsCompleted = t.IsCompleted,
                    DueDate = t.DueDate,
                    Priority = t.Priority,
                    CreatedAt = t.CreatedAt,
                    UserId = t.UserId,
                    Username = t.User!.Username
                })
                .ToListAsync();
        }
    }
}