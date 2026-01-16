using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskManagerAPI.DTOs;
using TaskManagerAPI.Services;

namespace TaskManagerAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class TasksController : ControllerBase
    {
        private readonly ITaskRepository _taskRepository;
        private readonly ILogger<TasksController> _logger;

        public TasksController(ITaskRepository taskRepository, ILogger<TasksController> logger)
        {
            _taskRepository = taskRepository;
            _logger = logger;
        }

        private int GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                throw new UnauthorizedAccessException("Invalid user ID in token");
            }
            return userId;
        }

        private string GetUserRole()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value ?? "User";
        }

        private bool IsAdmin()
        {
            return GetUserRole() == "Admin";
        }

        [HttpGet]
        public async Task<IActionResult> GetAllTasks()
        {
            try
            {
                var userId = GetUserId();
                _logger.LogInformation("User {UserId} ({Role}) fetching all tasks", userId, GetUserRole());

                if (IsAdmin())
                {
                    var allTasks = await _taskRepository.GetAllTasksAsync();
                    return Ok(new
                    {
                        Message = "Admin view: All tasks",
                        Count = allTasks.Count,
                        Tasks = allTasks
                    });
                }
                else
                {
                    var userTasks = await _taskRepository.GetUserTasksAsync(userId);
                    return Ok(new
                    {
                        Message = "Your tasks",
                        Count = userTasks.Count,
                        Tasks = userTasks
                    });
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tasks");
                return StatusCode(500, new { message = "An error occurred while fetching tasks" });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTask(int id)
        {
            try
            {
                var userId = GetUserId();
                _logger.LogInformation("User {UserId} fetching task {TaskId}", userId, id);

                var task = await _taskRepository.GetTaskByIdAsync(id);
                if (task == null)
                {
                    return NotFound(new { message = "Task not found" });
                }

                if (!IsAdmin() && task.UserId != userId)
                {
                    _logger.LogWarning("User {UserId} attempted to access task {TaskId} owned by user {TaskOwnerId}",
                        userId, id, task.UserId);
                    return Forbid();
                }

                return Ok(task);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching task {TaskId}", id);
                return StatusCode(500, new { message = "An error occurred while fetching the task" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskDto createTaskDto)
        {
            try
            {
                var userId = GetUserId();
                _logger.LogInformation("User {UserId} creating new task", userId);

                if (createTaskDto == null)
                {
                    return BadRequest(new { message = "Task data is required" });
                }

                if (string.IsNullOrWhiteSpace(createTaskDto.Title))
                {
                    return BadRequest(new { message = "Task title is required" });
                }

                if (createTaskDto.DueDate < DateTime.UtcNow.Date)
                {
                    return BadRequest(new { message = "Due date cannot be in the past" });
                }

                var task = await _taskRepository.CreateTaskAsync(createTaskDto, userId);

                _logger.LogInformation("Task {TaskId} created successfully for user {UserId}", task.Id, userId);
                return CreatedAtAction(nameof(GetTask), new { id = task.Id }, new
                {
                    Message = "Task created successfully",
                    Task = task
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating task");
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTask(int id, [FromBody] UpdateTaskDto updateTaskDto)
        {
            try
            {
                var userId = GetUserId();
                _logger.LogInformation("User {UserId} updating task {TaskId}", userId, id);

                var existingTask = await _taskRepository.GetTaskByIdAsync(id);
                if (existingTask == null)
                {
                    return NotFound(new { message = "Task not found" });
                }

                if (!IsAdmin() && existingTask.UserId != userId)
                {
                    _logger.LogWarning("User {UserId} attempted to update task {TaskId} owned by user {TaskOwnerId}",
                        userId, id, existingTask.UserId);
                    return Forbid();
                }

                if (updateTaskDto.DueDate.HasValue && updateTaskDto.DueDate.Value < DateTime.UtcNow.Date)
                {
                    return BadRequest(new { message = "Due date cannot be in the past" });
                }

                var task = await _taskRepository.UpdateTaskAsync(id, updateTaskDto);
                if (task == null)
                {
                    return NotFound(new { message = "Task not found" });
                }

                _logger.LogInformation("Task {TaskId} updated successfully by user {UserId}", id, userId);
                return Ok(new
                {
                    Message = "Task updated successfully",
                    Task = task
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating task {TaskId}", id);
                return StatusCode(500, new { message = "An error occurred while updating the task" });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTask(int id)
        {
            try
            {
                var userId = GetUserId();
                _logger.LogInformation("User {UserId} deleting task {TaskId}", userId, id);

                var existingTask = await _taskRepository.GetTaskByIdAsync(id);
                if (existingTask == null)
                {
                    return NotFound(new { message = "Task not found" });
                }

                if (!IsAdmin() && existingTask.UserId != userId)
                {
                    _logger.LogWarning("User {UserId} attempted to delete task {TaskId} owned by user {TaskOwnerId}",
                        userId, id, existingTask.UserId);
                    return Forbid();
                }

                var success = await _taskRepository.DeleteTaskAsync(id);
                if (!success)
                {
                    return NotFound(new { message = "Task not found" });
                }

                _logger.LogInformation("Task {TaskId} deleted successfully by user {UserId}", id, userId);
                return NoContent();
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting task {TaskId}", id);
                return StatusCode(500, new { message = "An error occurred while deleting the task" });
            }
        }

        [HttpGet("my")]
        public async Task<IActionResult> GetMyTasks()
        {
            try
            {
                var userId = GetUserId();
                _logger.LogInformation("User {UserId} fetching their tasks", userId);

                var tasks = await _taskRepository.GetUserTasksAsync(userId);
                return Ok(new
                {
                    Message = "Your tasks",
                    Count = tasks.Count,
                    Tasks = tasks
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user tasks");
                return StatusCode(500, new { message = "An error occurred while fetching your tasks" });
            }
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchTasks([FromQuery] string title)
        {
            try
            {
                var userId = GetUserId();
                _logger.LogInformation("User {UserId} searching tasks with title: {Title}", userId, title);

                if (string.IsNullOrWhiteSpace(title))
                {
                    return BadRequest(new { message = "Search term is required" });
                }

                IEnumerable<TaskDto> filteredTasks;

                if (IsAdmin())
                {
                    var allTasks = await _taskRepository.GetAllTasksAsync();
                    filteredTasks = allTasks.Where(t =>
                        t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    var userTasks = await _taskRepository.GetUserTasksAsync(userId);
                    filteredTasks = userTasks.Where(t =>
                        t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                }

                return Ok(new
                {
                    Message = "Search results",
                    SearchTerm = title,
                    Count = filteredTasks.Count(),
                    Tasks = filteredTasks
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching tasks");
                return StatusCode(500, new { message = "An error occurred while searching tasks" });
            }
        }

        [HttpGet("completed")]
        public async Task<IActionResult> GetCompletedTasks()
        {
            try
            {
                var userId = GetUserId();

                IEnumerable<TaskDto> completedTasks;

                if (IsAdmin())
                {
                    var allTasks = await _taskRepository.GetAllTasksAsync();
                    completedTasks = allTasks.Where(t => t.IsCompleted);
                }
                else
                {
                    var userTasks = await _taskRepository.GetUserTasksAsync(userId);
                    completedTasks = userTasks.Where(t => t.IsCompleted);
                }

                return Ok(new
                {
                    Message = "Completed tasks",
                    Count = completedTasks.Count(),
                    Tasks = completedTasks
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching completed tasks");
                return StatusCode(500, new { message = "An error occurred while fetching completed tasks" });
            }
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingTasks()
        {
            try
            {
                var userId = GetUserId();

                IEnumerable<TaskDto> pendingTasks;

                if (IsAdmin())
                {
                    var allTasks = await _taskRepository.GetAllTasksAsync();
                    pendingTasks = allTasks.Where(t => !t.IsCompleted);
                }
                else
                {
                    var userTasks = await _taskRepository.GetUserTasksAsync(userId);
                    pendingTasks = userTasks.Where(t => !t.IsCompleted);
                }

                return Ok(new
                {
                    Message = "Pending tasks",
                    Count = pendingTasks.Count(),
                    Tasks = pendingTasks
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching pending tasks");
                return StatusCode(500, new { message = "An error occurred while fetching pending tasks" });
            }
        }

        [HttpGet("priority/{priority}")]
        public async Task<IActionResult> GetTasksByPriority(string priority)
        {
            try
            {
                var userId = GetUserId();
                _logger.LogInformation("User {UserId} fetching tasks with priority: {Priority}", userId, priority);

                var validPriorities = new[] { "Low", "Medium", "High" };
                if (!validPriorities.Contains(priority, StringComparer.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        message = "Invalid priority. Must be: Low, Medium, or High",
                        validPriorities
                    });
                }

                IEnumerable<TaskDto> priorityTasks;

                if (IsAdmin())
                {
                    var allTasks = await _taskRepository.GetAllTasksAsync();
                    priorityTasks = allTasks.Where(t =>
                        t.Priority.Equals(priority, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    var userTasks = await _taskRepository.GetUserTasksAsync(userId);
                    priorityTasks = userTasks.Where(t =>
                        t.Priority.Equals(priority, StringComparison.OrdinalIgnoreCase));
                }

                return Ok(new
                {
                    Message = $"Tasks with {priority} priority",
                    Priority = priority,
                    Count = priorityTasks.Count(),
                    Tasks = priorityTasks
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching priority tasks");
                return StatusCode(500, new { message = "An error occurred while fetching priority tasks" });
            }
        }

        [HttpPatch("{id}/complete")]
        public async Task<IActionResult> MarkTaskComplete(int id)
        {
            try
            {
                var userId = GetUserId();
                _logger.LogInformation("User {UserId} marking task {TaskId} as complete", userId, id);

                var existingTask = await _taskRepository.GetTaskByIdAsync(id);
                if (existingTask == null)
                {
                    return NotFound(new { message = "Task not found" });
                }

                if (!IsAdmin() && existingTask.UserId != userId)
                {
                    _logger.LogWarning("User {UserId} attempted to complete task {TaskId} owned by user {TaskOwnerId}",
                        userId, id, existingTask.UserId);
                    return Forbid();
                }

                var updateDto = new UpdateTaskDto { IsCompleted = true };
                var task = await _taskRepository.UpdateTaskAsync(id, updateDto);

                if (task == null)
                {
                    return NotFound(new { message = "Task not found" });
                }

                _logger.LogInformation("Task {TaskId} marked as complete by user {UserId}", id, userId);
                return Ok(new
                {
                    Message = "Task marked as complete",
                    Task = task
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing task {TaskId}", id);
                return StatusCode(500, new { message = "An error occurred while completing the task" });
            }
        }

        [HttpPatch("{id}/incomplete")]
        public async Task<IActionResult> MarkTaskIncomplete(int id)
        {
            try
            {
                var userId = GetUserId();
                _logger.LogInformation("User {UserId} marking task {TaskId} as incomplete", userId, id);

                var existingTask = await _taskRepository.GetTaskByIdAsync(id);
                if (existingTask == null)
                {
                    return NotFound(new { message = "Task not found" });
                }

                if (!IsAdmin() && existingTask.UserId != userId)
                {
                    _logger.LogWarning("User {UserId} attempted to mark task {TaskId} as incomplete (owned by user {TaskOwnerId})",
                        userId, id, existingTask.UserId);
                    return Forbid();
                }

                var updateDto = new UpdateTaskDto { IsCompleted = false };
                var task = await _taskRepository.UpdateTaskAsync(id, updateDto);

                if (task == null)
                {
                    return NotFound(new { message = "Task not found" });
                }

                _logger.LogInformation("Task {TaskId} marked as incomplete by user {UserId}", id, userId);
                return Ok(new
                {
                    Message = "Task marked as incomplete",
                    Task = task
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                return Unauthorized(new { message = "Authentication failed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking task {TaskId} as incomplete", id);
                return StatusCode(500, new { message = "An error occurred while updating the task" });
            }
        }
    }
}