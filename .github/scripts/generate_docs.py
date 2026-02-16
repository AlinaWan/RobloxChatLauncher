import re

def generate_markdown(source_file, output_file):
    with open(source_file, 'r', encoding='utf-8') as f:
        content = f.read()

    # Find the entire switch block
    switch_match = re.search(r'switch\s*\(command\)\s*\{(.*?)default:', content, re.DOTALL)
    if not switch_match:
        print("Oops! I couldn't find the command switch block... Check the script for me babe? 🎀")
        return

    switch_body = switch_match.group(1)
    
    # 1. Matches one or more 'case' lines
    # 2. Captures everything until it hits the next 'case' or 'return true;'
    pattern = re.compile(r'((?:case\s+"/[^"]+":\s*\n\s*)+)(.*?)(?=case|return\s+true;)', re.DOTALL)
    
    commands = []
    for match in pattern.finditer(switch_body):
        case_block = match.group(1)
        logic_block = match.group(2).strip()
        
        # Extract commands from the case block
        cases = re.findall(r'case\s+"/([^"]+)"', case_block)
        
        # Clean up the logic: remove awaits, trailing semicolons, and fix newlines
        clean_logic = logic_block.replace('await ', '').rstrip(';')
        clean_logic = " ".join(clean_logic.split()) # Remove internal newlines/tabs
        
        commands.append({
            "command": f"/{cases[0]}",
            "aliases": ", ".join([f"`/{c}`" for c in cases[1:]]) if len(cases) > 1 else "None",
            "action": f"```{clean_logic}```"
        })

    # Build Markdown table
    md = "# Command Documentation\n\n"
    md += "| Command | Aliases | Action / Function |\n"
    md += "| :--- | :--- | :--- |\n"
    for cmd in commands:
        md += f"| **{cmd['command']}** | {cmd['aliases']} | {cmd['action']} |\n"

    with open(output_file, 'w', encoding='utf-8') as f:
        f.write(md)

if __name__ == "__main__":
    generate_markdown('client/ChatForm.Commands.cs', 'assets/docs/COMMANDS.md')