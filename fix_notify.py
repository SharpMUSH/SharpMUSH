import re
import os

# Files to process
TEST_DIR = "/home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests"

def process_file(filepath):
    with open(filepath, 'r') as f:
        content = f.read()
    
    original = content
    
    # Parse methods - track [Skip], CreateTestPlayer, and make substitutions
    lines = content.split('\n')
    result_lines = []
    
    # State tracking
    in_method = False
    method_brace_depth = 0
    brace_depth = 0
    is_skipped = False
    has_pending_skip = False
    method_start_idx = -1
    method_body_start = -1
    executor_var = None  # The executor var for the current method
    executor_injected = False
    needs_executor_injection = False
    
    # For method-level context  
    method_lines = []
    
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()
        
        # Check for Skip attribute
        if re.search(r'\[Skip\b', stripped):
            has_pending_skip = True
        
        # Detect method start: public async ValueTask/Task
        if re.search(r'public\s+async\s+(ValueTask|Task)\b', line) and not in_method:
            in_method = True
            is_skipped = has_pending_skip
            has_pending_skip = False
            method_brace_depth = brace_depth
            executor_var = None
            executor_injected = False
            needs_executor_injection = False
            method_body_start = -1
        
        # Track brace depth
        open_braces = line.count('{')
        close_braces = line.count('}')
        
        if in_method and method_body_start == -1 and '{' in line:
            method_body_start = i
        
        # Check for CreateTestPlayerAsync in method  
        if in_method and not is_skipped:
            m = re.search(r'var\s+(\w+)\s*=\s*await\s+CreateTestPlayerAsync\(', line)
            if m:
                executor_var = m.group(1)  # e.g. testPlayer or player - is a DBRef
            m2 = re.search(r'var\s+(\w+)\s*=\s*await\s+CreateTestPlayerWithHandleAsync\(', line)
            if m2:
                executor_var = m2.group(1) + ".DbRef"
            
            # Check if executor already declared
            if re.search(r'var\s+executor\s*=', line):
                executor_injected = True
        
        # Make Notify substitutions (only in non-skipped methods)
        new_line = line
        if in_method and not is_skipped:
            # Pattern 1: .Notify(Arg.Any<DBRef>(), 
            if re.search(r'\.Notify\(Arg\.Any<DBRef>\(\)', line):
                if executor_var:
                    new_line = re.sub(r'\.Notify\(Arg\.Any<DBRef>\(\)', '.Notify(' + executor_var, new_line)
                else:
                    new_line = re.sub(r'\.Notify\(Arg\.Any<DBRef>\(\)', '.Notify(executor', new_line)
                    needs_executor_injection = True
            
            # Pattern 2: .Notify(Arg.Any<AnySharpObject>(), on same line as Notify(
            if re.search(r'\.Notify\(Arg\.Any<AnySharpObject>\(\)', line):
                if executor_var:
                    new_line = re.sub(r'\.Notify\(Arg\.Any<AnySharpObject>\(\)', 
                                     '.Notify(TestHelpers.MatchingObject(' + executor_var + ')', new_line)
                else:
                    new_line = re.sub(r'\.Notify\(Arg\.Any<AnySharpObject>\(\)', 
                                     '.Notify(TestHelpers.MatchingObject(executor)', new_line)
                    needs_executor_injection = True
            
            # Pattern 3: .Notify( on its own line, then next line is Arg.Any<AnySharpObject>()
            # Check if PREVIOUS line ended with .Notify( and this line starts with Arg.Any<AnySharpObject>
            if i > 0:
                prev_stripped = lines[i-1].strip() if i > 0 else ''
                if prev_stripped == '.Notify(' and stripped.startswith('Arg.Any<AnySharpObject>(),'):
                    indent = len(line) - len(line.lstrip())
                    indent_str = line[:indent]
                    if executor_var:
                        new_line = indent_str + 'TestHelpers.MatchingObject(' + executor_var + '),' + line[indent + len('Arg.Any<AnySharpObject>(),'):]
                    else:
                        new_line = indent_str + 'TestHelpers.MatchingObject(executor),' + line[indent + len('Arg.Any<AnySharpObject>(),'):]
                        needs_executor_injection = True
        
        result_lines.append(new_line)
        
        brace_depth += open_braces - close_braces
        
        # Check if method ended
        if in_method and brace_depth <= method_brace_depth and method_body_start != -1:
            in_method = False
            is_skipped = False
            executor_var = None
        
        i += 1
    
    new_content = '\n'.join(result_lines)
    
    # Now inject executor variable into methods that need it
    # We need to do a second pass to inject the executor var
    # Find method bodies where we used 'executor' but it wasn't declared
    if 'executor' in new_content and 'var executor = WebAppFactoryArg.ExecutorDBRef' not in new_content and 'WebAppFactoryArg' in new_content:
        # Need to inject executor in each method that uses it
        new_content = inject_executor_vars(new_content)
    
    if new_content != original:
        with open(filepath, 'w') as f:
            f.write(new_content)
        print(f"Modified: {filepath}")
    
    return new_content != original

def inject_executor_vars(content):
    """Inject 'var executor = WebAppFactoryArg.ExecutorDBRef;' into methods that use executor but don't declare it."""
    lines = content.split('\n')
    result = []
    
    brace_depth = 0
    in_method = False
    method_brace_depth = 0
    method_body_opened = False
    method_uses_executor = False
    method_has_executor_decl = False
    is_skipped = False
    has_pending_skip = False
    
    # First, find all method ranges that use executor but don't declare it
    # We need to process in two passes
    # Pass 1: find method start/end lines and whether they need injection
    
    method_info = []  # list of (start_line, end_line, needs_injection)
    
    i = 0
    cur_method_start = -1
    cur_method_body_start = -1
    cur_uses_executor = False
    cur_has_decl = False
    cur_skipped = False
    
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()
        
        if re.search(r'\[Skip\b', stripped):
            has_pending_skip = True
        
        if re.search(r'public\s+async\s+(ValueTask|Task)\b', line) and not in_method:
            in_method = True
            cur_skipped = has_pending_skip
            has_pending_skip = False
            method_brace_depth = brace_depth
            cur_method_start = i
            cur_method_body_start = -1
            cur_uses_executor = False
            cur_has_decl = False
            method_body_opened = False
        
        if in_method and not method_body_opened and '{' in line:
            # Find the method body opening brace
            cur_method_body_start = i
            method_body_opened = True
        
        open_b = line.count('{')
        close_b = line.count('}')
        
        if in_method:
            if re.search(r'\bexecutor\b', line) and 'var executor' not in line and '// executor' not in line:
                cur_uses_executor = True
            if re.search(r'var\s+executor\s*=', line):
                cur_has_decl = True
        
        brace_depth += open_b - close_b
        
        if in_method and brace_depth <= method_brace_depth and method_body_opened:
            # Method ended
            method_info.append({
                'start': cur_method_start,
                'body_start': cur_method_body_start,
                'end': i,
                'uses_executor': cur_uses_executor,
                'has_decl': cur_has_decl,
                'skipped': cur_skipped
            })
            in_method = False
            method_body_opened = False
            cur_method_start = -1
        
        i += 1
    
    # Pass 2: inject executor where needed
    injection_lines = set()
    for m in method_info:
        if m['uses_executor'] and not m['has_decl'] and not m['skipped'] and m['body_start'] >= 0:
            # Find the line right after the opening brace to inject executor
            body_line = m['body_start']
            # The opening brace might be at end of signature line or on its own line
            # We want to inject AFTER the opening { line
            injection_lines.add(body_line)
    
    if not injection_lines:
        return content
    
    result = []
    for i, line in enumerate(lines):
        result.append(line)
        if i in injection_lines:
            # Determine indentation from the method body
            # Look at next non-empty line for indentation
            indent = '\t\t'  # default
            for j in range(i+1, min(i+5, len(lines))):
                if lines[j].strip():
                    indent = lines[j][:len(lines[j]) - len(lines[j].lstrip())]
                    break
            result.append(indent + 'var executor = WebAppFactoryArg.ExecutorDBRef;')
    
    return '\n'.join(result)


# Process all files
files_to_process = []
for root, dirs, files in os.walk(TEST_DIR):
    for f in files:
        if f.endswith('.cs'):
            files_to_process.append(os.path.join(root, f))

# Filter to only the ones that have the patterns we need
target_files = []
for f in files_to_process:
    with open(f, 'r') as fp:
        content = fp.read()
    if '.Notify(Arg.Any<DBRef>()' in content or '.Notify(Arg.Any<AnySharpObject>()' in content:
        target_files.append(f)

print(f"Found {len(target_files)} files to process")
for f in target_files:
    process_file(f)

print("Done!")
