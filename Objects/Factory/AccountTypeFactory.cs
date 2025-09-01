using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;


namespace NetworkMonitor.Objects.Factory;

public class AccountTypeFactory
{
    // Configuration for different account types
    // inside AccountTypeFactory
    private static readonly Dictionary<string, (int hostLimit, int tokenLimit, int dailyTokens, int contextSize)>
        _accountTypeConfigurations = PlanCatalog.BuildAccountTypeConfigurations();

    public static List<T> GetFunctionsForAccountType<T>(string accountType,
                                                                      T fn_are_functions_running,
                                                                      T fn_cancel_functions,
                                                                      T fn_add_host,
                                                                      T fn_edit_host,
                                                                      T fn_get_host_data,
                                                                      T fn_get_host_list,
                                                                      T fn_get_user_info,
                                                                      T fn_get_agents,
                                                                      T fn_call_security_expert,
                                                                      T fn_call_penetration_expert,
                                                                      T fn_call_search_expert,
                                                                      T fn_call_cmd_processor_expert,
                                                                      T fn_call_quantum_expert,
                                                                      T fn_run_busybox
                                                                      )
    {
        return new List<T>
            {
                fn_are_functions_running,
                fn_cancel_functions,
                fn_add_host,
                fn_edit_host,
                fn_get_host_data,
                fn_get_host_list,
                fn_get_user_info,
                fn_get_agents,
                fn_call_security_expert,
                fn_call_penetration_expert,
                fn_call_search_expert,
                fn_call_cmd_processor_expert,
                fn_call_quantum_expert,
                fn_run_busybox,
            };
    }
    /*  return accountType switch
    {
        "Free" => new List<T>
        {
            fn_are_functions_running,
            fn_add_host,
            fn_edit_host,
            fn_get_host_data,
            fn_get_host_list,
            fn_get_user_info,
            fn_get_agents
        },
        "Standard" => new List<T>
        {
            fn_are_functions_running,
            fn_add_host,
            fn_edit_host,
            fn_get_host_data,
            fn_get_host_list,
            fn_get_user_info,
            fn_get_agents,
            fn_call_security_expert,
            fn_call_search_expert,
        },
        "Professional" => new List<T>
        {
            fn_are_functions_running,
            fn_add_host,
            fn_edit_host,
            fn_get_host_data,
            fn_get_host_list,
            fn_get_user_info,
            fn_get_agents,
            fn_call_security_expert,
            fn_call_penetration_expert,
            fn_call_search_expert,
        },
        "Enterprise" => new List<T>
        {
            fn_are_functions_running,
            fn_add_host,
            fn_edit_host,
            fn_get_host_data,
            fn_get_host_list,
            fn_get_user_info,
            fn_get_agents,
            fn_call_security_expert,
            fn_call_penetration_expert,
            fn_run_busybox,
            fn_call_search_expert,
        },
        "God" => new List<T>
        {
            fn_are_functions_running,
            fn_add_host,
            fn_edit_host,
            fn_get_host_data,
            fn_get_host_list,
            fn_get_user_info,
            fn_get_agents,
            fn_call_security_expert,
            fn_call_penetration_expert,
            fn_run_busybox,
            fn_call_search_expert,
        },
        _ => throw new ArgumentException("Invalid or unsupported account type.")
    };*/

    public static Dictionary<string, string> GetFunctionCommandMap(string llmRunnerType)
    {
        return llmRunnerType switch
        {
            "TestLLM" => GetFunctionCommandMapTestLLM(),
            "TurboLLM" => GetFunctionCommandMapTurboLLM(),
            "HugLLM" => GetFunctionCommandMapHugLLM(),
            _ => new Dictionary<string, string>()
        };

    }
    public static Dictionary<string, string> GetFunctionCommandMapTurboLLM()
    {
        return new Dictionary<string, string>
                {
                    { "call_security_expert", "nmap" },
                    { "call_penetration_expert", "meta" },
                    { "call_search_expert", "search" },
                    { "call_cmd_processor_expert", "cmdprocessor" },
                    { "call_quantum_expert", "quantum" },
                    { "run_busybox_command", "busybox" }
                };
    }
    public static Dictionary<string, string> GetFunctionCommandMapHugLLM()
    {
        return new Dictionary<string, string>
                {
                    { "call_security_expert", "nmap" },
                    { "call_penetration_expert", "meta" },
                    { "call_search_expert", "search" },
                    { "call_cmd_processor_expert", "cmdprocessor" },
                    { "call_quantum_expert", "quantum" },
                    { "run_busybox_command", "busybox" }
                };
    }

    public static Dictionary<string, string> GetFunctionCommandMapTestLLM()
    {
        return new Dictionary<string, string>
                {
                    {"run_search_web", "search" },
                    {"run_crawl_page", "search" },
                    {"run_crawl_site", "search" },
                    {"run_nmap", "nmap" },
                    {"run_openssl", "nmap" },
                    {"search_metasploit_modules", "meta" },
                    {"get_metasploit_module_info", "meta" },
                    {"run_metasploit", "meta" },
                    {"call_cmd_processor_expert", "cmdprocessor" },
                    {"run_busybox_command", "busybox" }
                };
    }
    public static List<string> GetFunctionNamesForAccountType(string accountType)
    {
        return accountType switch
        {
            "Free" => new List<string>
        {
             "are_functions_running",
            "cancel_functions",
            "add_host",
            "edit_host",
            "get_host_data",
            "get_host_list",
            "get_user_info",
            "get_agents",
            "call_security_expert",
            "call_monitor_sys",
            "call_search_expert",
            "call_cmd_processor_expert",
            "call_quantum_expert",
            "run_search_web",
            "run_crawl_page",
            "run_crawl_site",
            "run_nmap",
            "run_openssl",
            "run_cmd_processor",
            "add_cmd_processor",
            "update_cmd_processor",
            "delete_cmd_processor",
            "get_cmd_processor_help",
            "get_cmd_processor_list",
            "get_cmd_processor_source_code",
            "test_quantum_safety",
            "scan_quantum_ports",
            "get_quantum_algorithm_info",
            "validate_quantum_config",
            "call_security_basic_flow",
            "call_penetration_flow",
            "call_cmd_processor_builder_flow",
            "execute_query*",
            "cp_*"
        },
            "Standard" => new List<string>
        {
            "are_functions_running",
            "cancel_functions",
            "add_host",
            "edit_host",
            "get_host_data",
            "get_host_list",
            "get_user_info",
            "get_agents",
            "call_security_expert",
            "call_monitor_sys",
            "call_search_expert",
            "call_cmd_processor_expert",
            "call_quantum_expert",
            "run_search_web",
            "run_crawl_page",
            "run_crawl_site",
            "run_nmap",
            "run_openssl",
            "run_cmd_processor",
            "add_cmd_processor",
            "update_cmd_processor",
            "delete_cmd_processor",
            "get_cmd_processor_help",
            "get_cmd_processor_list",
            "get_cmd_processor_source_code",
            "test_quantum_safety",
            "scan_quantum_ports",
            "get_quantum_algorithm_info",
            "validate_quantum_config",
            "call_security_basic_flow",
            "call_penetration_flow",
            "call_cmd_processor_builder_flow",
            "execute_query*",
            "cp_*"
        },
            "Professional" => new List<string>
        {
            "are_functions_running",
            "cancel_functions",
            "add_host",
            "edit_host",
            "get_host_data",
            "get_host_list",
            "get_user_info",
            "get_agents",
            "call_security_expert",
            "call_penetration_expert",
            "call_cmd_processor_expert",
            "call_monitor_sys",
            "call_search_expert",
            "call_quantum_expert",
            "run_search_web",
            "run_crawl_page",
            "run_crawl_site",
            "run_nmap",
            "run_openssl",
            "search_metasploit_modules",
            "get_metasploit_module_info",
            "run_metasploit",
            "run_cmd_processor",
            "add_cmd_processor",
            "update_cmd_processor",
            "delete_cmd_processor",
            "get_cmd_processor_help",
            "get_cmd_processor_list",
            "get_cmd_processor_source_code",
            "test_quantum_safety",
            "scan_quantum_ports",
            "get_quantum_algorithm_info",
            "validate_quantum_config",
            "call_security_basic_flow",
            "call_penetration_flow",
            "call_cmd_processor_builder_flow",
            "execute_query*",
            "cp_*"
        },
            "Enterprise" => new List<string>
        {
            "are_functions_running",
            "cancel_functions",
            "add_host",
            "edit_host",
            "get_host_data",
            "get_host_list",
            "get_user_info",
            "get_agents",
            "call_security_expert",
            "call_penetration_expert",
            "call_cmd_processor_expert",
            "call_monitor_sys",
            "call_quantum_expert",
            "run_busybox",
            "call_search_expert",
            "run_search_web",
            "run_crawl_page",
            "run_crawl_site",
            "run_nmap",
            "run_openssl",
            "search_metasploit_modules",
            "get_metasploit_module_info",
            "run_metasploit",
            "run_busybox_command",
            "add_cmd_processor",
            "update_cmd_processor",
            "run_cmd_processor",
            "delete_cmd_processor",
            "add_cmd_processor",
            "get_cmd_processor_help",
            "get_cmd_processor_list",
            "get_cmd_processor_source_code",
            "test_quantum_safety",
            "scan_quantum_ports",
            "get_quantum_algorithm_info",
            "validate_quantum_config",
            "call_security_basic_flow",
            "call_penetration_flow",
            "call_cmd_processor_builder_flow",
            "execute_query*",
            "cp_*"
        },
            "God" => new List<string>
        {
            "are_functions_running",
            "cancel_functions",
            "add_host",
            "edit_host",
            "get_host_data",
            "get_host_list",
            "get_user_info",
            "get_agents",
            "call_security_expert",
            "call_penetration_expert",
            "call_cmd_processor_expert",
            "call_monitor_sys",
            "call_quantum_expert",
            "run_busybox",
            "call_search_expert",
            "run_search_web",
            "run_crawl_page",
            "run_crawl_site",
            "run_nmap",
            "run_openssl",
            "search_metasploit_modules",
            "get_metasploit_module_info",
            "run_metasploit",
            "run_busybox_command",
            "add_cmd_processor",
            "update_cmd_processor",
            "run_cmd_processor",
            "delete_cmd_processor",
            "add_cmd_processor",
            "get_cmd_processor_help",
            "get_cmd_processor_list",
            "get_cmd_processor_source_code",
            "test_quantum_safety",
            "scan_quantum_ports",
            "get_quantum_algorithm_info",
            "validate_quantum_config",
            "call_security_basic_flow",
            "call_penetration_flow",
            "call_cmd_processor_builder_flow",
            "execute_query*",
            "cp_*"
        },
            _ => new List<string>
        {
            "are_functions_running",
            "cancel_functions",
            "add_host",
            "edit_host",
            "get_host_data",
            "get_host_list",
            "get_user_info",
            "get_agents",
            "get_cmd_processor_help",
            "get_cmd_processor_list",
            "get_cmd_processor_source_code",
            "run_cmd_processor",
            "test_quantum_safety",
            "scan_quantum_ports",
            "get_quantum_algorithm_info",
            "validate_quantum_config",
            "call_quantum_expert",
            "call_monitor_sys",
            "call_security_basic_flow",
            "call_penetration_flow",
            "call_cmd_processor_builder_flow",
            "execute_query*",
            "cp_*"
        }
        };
    }


    public static bool IsFunctionNameAllowed(IEnumerable<string> allowedPatterns, string funcName)
    {
        foreach (var pattern in allowedPatterns)
        {
            if (pattern.Contains("*"))
            {
                // Replace "*" with ".*" and escape the rest, anchor to start/end
                string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                if (Regex.IsMatch(funcName, regex, RegexOptions.IgnoreCase))
                    return true;
            }
            else
            {
                if (string.Equals(pattern, funcName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    public static string? GetLowestAccountTypeForFunction(string funcName)
    {
        // Define the order of account types from lowest to highest
        var accountTypesOrder = new List<string> { "Free", "Standard", "Professional", "Enterprise" };

        foreach (var accountType in accountTypesOrder)
        {
            var availableFuncs = GetFunctionNamesForAccountType(accountType);
            if (availableFuncs != null && IsFunctionNameAllowed(availableFuncs, funcName))
            {
                return accountType;
            }

        }

        // If the function is not found in any account type, return null or throw an exception
        return null; // or throw new ArgumentException($"Function '{funcName}' is not available for any account type.");
    }

    public static (bool, string) CheckUserFuncPermissions(UserInfo user, string funcName, string frontendUrl)
    {
        if (string.IsNullOrEmpty(frontendUrl)) frontendUrl = AppConstants.FrontendUrl;
        if (user == null) return (false, $"User is null in CheckUserFuncPermissions. Contact support with details of this error");

        var allFuncs = GetFunctionNamesForAccountType("God");
        if (!IsFunctionNameAllowed(allFuncs, funcName))
        {
            return (false, $"There is no `{funcName}` function. You are trying to call a non existent function");
        }

        string message = "";
        string? accountType = user.AccountType;
        if (string.IsNullOrEmpty(accountType) || accountType == "Default")
        {

            var freeAvailableFuncs = GetFunctionNamesForAccountType("Free");
            string extraMessage = "";
            if (freeAvailableFuncs != null && freeAvailableFuncs.Any(a => a == funcName))
            {
                extraMessage = $"  Ask the user to login. Visit [Quantum Network Monitor]({AppConstants.FrontendUrl}/Dashboard/#assistant=open&openInNewTab)and click login top right. They will gain access to this function along with many other benefits.";
            }
            else
            {
                string? lowestAccountType = GetLowestAccountTypeForFunction(funcName) ?? "";
                if (lowestAccountType != null)
                {
                    extraMessage = $" If they wish to use this advanced feature they will need to [Login]({AppConstants.FrontendUrl}/Dashboard/#assistant=open&openInNewTab)and upgrade to the {lowestAccountType} plan. Upgrade your subscription to enjoy access to this function and many more benefits, Visit [Subscription](https://{frontendUrl}/subscription#openInNewTab))for details";

                    if (lowestAccountType == "Standard") extraMessage += " . You can get a 'Free' upgrade to a Standard Plan if you Download and install the Quantum Network Monitor Agent. Visit [Download](https://{frontendUrl}/download#openInNewTab))and follow the instructions on installing any of the agents to get your Free Upgrade";


                }
                else extraMessage = $" Error : could not find an account type or plan that has access to the functon {funcName}";
            }
            message = $"For secuitry reasons users that are not logged in do not have access to `{funcName}` . {extraMessage}";

            accountType = "Default";
        }
        else message = $"User does not have access to the `{funcName}` function. Explain to the user they have several options: 1: Upgrade your subscription to enjoy access to this function and many more benefits, Visit [Subscription](https://{frontendUrl}/subscription#openInNewTab))for details. Or they can get a 'Free' upgrade to a Standard Plan if you Download and install the Quantum Network Monitor Agent. Visit [Download](https://{frontendUrl}/download#openInNewTab))and follow the instructions on installing any of the agents to get your Free Upgrade.";

        var availableFuncs = GetFunctionNamesForAccountType(accountType);
        if (availableFuncs != null && IsFunctionNameAllowed(availableFuncs, funcName))
        {
            return (true, $"User has access to the `{funcName}` function");
        }
        else
        {
            return (false, message);
        }

    }

    public static IEnumerable<AccountType> GetAccountTypes()
    {
        return _accountTypeConfigurations.Select(kvp => new AccountType(kvp.Key, kvp.Value.hostLimit, kvp.Value.tokenLimit, kvp.Value.dailyTokens, kvp.Value.contextSize));
    }

    public static AccountType GetAccountTypeByName(string name)
    {
        if (_accountTypeConfigurations.TryGetValue(name, out var config))
        {
            return new AccountType(name, config.hostLimit, config.tokenLimit, config.dailyTokens, config.contextSize);
        }
        else
        {
            return new AccountType("Default", config.hostLimit, config.tokenLimit, config.dailyTokens, config.contextSize);

        }
    }


    public static string GetPermissionSuffix(string accountType, string sessionStartName, string chainStartName)
    {
        if (sessionStartName == chainStartName)
        {
            return accountType switch
            {
                "Standard" => "_standard",
                "Professional" => "_professional",
                "Professional-Old" => "_professional",
                "Enterprise" => "_professional",
                "God" => "_professional",
                _ => ""
            };
        }
        return "";
    }
}

public class AccountType
{
    public int HostLimit { get; set; }
    public string Name { get; set; }
    public int TokenLimit { get; set; }
    public int DailyTokens { get; set; }
    public int ContextSize { get; set; }

    public AccountType(string name, int hostLimit, int tokenLimit, int dailyTokens, int contextSize)
    {
        Name = name;
        HostLimit = hostLimit;
        TokenLimit = tokenLimit;
        DailyTokens = dailyTokens;
        ContextSize = contextSize;
    }
}