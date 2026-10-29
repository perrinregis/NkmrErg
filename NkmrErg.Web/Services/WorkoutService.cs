using System.Text.Json;
using NkmrErg.Web.Models;

namespace NkmrErg.Web.Services
{
    public interface IWorkoutService
    {
        Task<List<MyWhooshWorkout>> GetWorkoutsAsync();
        Task<MyWhooshWorkout?> GetWorkoutByIdAsync(string id);
        Task SaveWorkoutAsync(MyWhooshWorkout workout);
        Task DeleteWorkoutAsync(string id);
        Task<MyWhooshWorkout?> ImportFromJsonAsync(string jsonContent);
        Task<int> GetFtpAsync();
        Task SetFtpAsync(int ftp);
        Task ResetToSampleWorkoutAsync();
    }

    public class WorkoutService : IWorkoutService
    {
        private const string WorkoutsStorageKey = "SavedWorkouts";
        private const string FtpStorageKey = "UserFTP";
        private readonly ILocalStorageService _localStorage;

        public WorkoutService(ILocalStorageService localStorage)
        {
            _localStorage = localStorage;
        }

        public async Task<int> GetFtpAsync()
        {
            var ftp = await _localStorage.GetItemAsync<int?>(FtpStorageKey);
            return (ftp.HasValue && ftp.Value > 50) ? ftp.Value : 200; // 200W par défaut
        }

        public async Task SetFtpAsync(int ftp)
        {
            if (ftp < 50) ftp = 50;
            if (ftp > 600) ftp = 600;
            await _localStorage.SetItemAsync(FtpStorageKey, ftp);
        }

        public async Task<List<MyWhooshWorkout>> GetWorkoutsAsync()
        {
            var workouts = await _localStorage.GetItemAsync<List<MyWhooshWorkout>>(WorkoutsStorageKey);
            var defaults = GetDefaultWorkouts();

            if (workouts == null || workouts.Count == 0)
            {
                workouts = new List<MyWhooshWorkout>(defaults);
                await _localStorage.SetItemAsync(WorkoutsStorageKey, workouts);
                return workouts;
            }

            // Si l'utilisateur avait l'ancien entraînement unique "hard-starts-2", on le remplace par les 3 séances par défaut
            var hasOldSample = workouts.Any(w => w.Id == "2037424" || w.Name == "hard-starts-2");
            if (hasOldSample)
            {
                workouts.RemoveAll(w => w.Id == "2037424" || w.Name == "hard-starts-2");
                foreach (var d in defaults)
                {
                    if (!workouts.Any(w => w.Id == d.Id || w.Name == d.Name))
                    {
                        workouts.Add(d);
                    }
                }
                await _localStorage.SetItemAsync(WorkoutsStorageKey, workouts);
            }

            return workouts;
        }

        public async Task<MyWhooshWorkout?> GetWorkoutByIdAsync(string id)
        {
            var workouts = await GetWorkoutsAsync();
            return workouts.FirstOrDefault(w => w.Id == id);
        }

        public async Task SaveWorkoutAsync(MyWhooshWorkout workout)
        {
            var workouts = await GetWorkoutsAsync();
            var existingIndex = workouts.FindIndex(w => w.Id == workout.Id);
            if (existingIndex >= 0)
            {
                workouts[existingIndex] = workout;
            }
            else
            {
                workouts.Add(workout);
            }
            await _localStorage.SetItemAsync(WorkoutsStorageKey, workouts);
        }

        public async Task DeleteWorkoutAsync(string id)
        {
            var workouts = await GetWorkoutsAsync();
            workouts.RemoveAll(w => w.Id == id);
            await _localStorage.SetItemAsync(WorkoutsStorageKey, workouts);
        }

        public async Task<MyWhooshWorkout?> ImportFromJsonAsync(string jsonContent)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var workout = JsonSerializer.Deserialize<MyWhooshWorkout>(jsonContent, options);
                if (workout != null && workout.WorkoutStepsArray.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(workout.Id))
                    {
                        workout.Id = Guid.NewGuid().ToString();
                    }
                    if (workout.Time <= 0)
                    {
                        workout.Time = workout.WorkoutStepsArray.Sum(s => s.Time);
                    }
                    workout.StepCount = workout.WorkoutStepsArray.Count;
                    await SaveWorkoutAsync(workout);
                    return workout;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur parsing JSON MyWhoosh : {ex.Message}");
            }
            return null;
        }

        public async Task ResetToSampleWorkoutAsync()
        {
            var defaults = GetDefaultWorkouts();
            await _localStorage.SetItemAsync(WorkoutsStorageKey, defaults);
        }

        public static List<MyWhooshWorkout> GetDefaultWorkouts()
        {
            var list = new List<MyWhooshWorkout>();
            var assembly = typeof(WorkoutService).Assembly;
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && name.Contains("Workouts"))
                .OrderBy(n => n)
                .ToList();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            foreach (var name in resourceNames)
            {
                try
                {
                    using var stream = assembly.GetManifestResourceStream(name);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        var json = reader.ReadToEnd();
                        var workout = JsonSerializer.Deserialize<MyWhooshWorkout>(json, options);
                        if (workout != null)
                        {
                            if (workout.Time <= 0 && workout.WorkoutStepsArray != null)
                            {
                                workout.Time = workout.WorkoutStepsArray.Sum(s => s.Time);
                            }
                            if (workout.StepCount <= 0 && workout.WorkoutStepsArray != null)
                            {
                                workout.StepCount = workout.WorkoutStepsArray.Count;
                            }
                            list.Add(workout);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur chargement workout embarqué '{name}': {ex.Message}");
                }
            }

            return list;
        }
    }
}
