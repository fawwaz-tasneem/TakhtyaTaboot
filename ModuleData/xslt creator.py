import csv
import os
import glob
import io

# --- CONFIGURATION ---
WORKING_DIR = '.' 

XSLT_HEADER = """<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
    <xsl:output omit-xml-declaration="yes"/>

    <xsl:template match="@*|node()">
        <xsl:copy>
            <xsl:apply-templates select="@*|node()"/>
        </xsl:copy>
    </xsl:template>
"""

XSLT_FOOTER = """
</xsl:stylesheet>"""

def read_file_safely(filepath):
    """
    Attempts to read a file with multiple encodings to handle 
    Excel's default Windows-1252 vs Standard UTF-8.
    """
    # Priority list: UTF-8 (Standard), cp1252 (Excel/Windows), latin1 (Fallback)
    encodings_to_try = ['utf-8-sig', 'cp1252', 'latin1']
    
    for enc in encodings_to_try:
        try:
            with open(filepath, mode='r', encoding=enc) as f:
                content = f.read()
            return content, enc
        except UnicodeDecodeError:
            continue
            
    return None, None

def process_file_pair(csv_path, xml_filename):
    base_name = os.path.splitext(xml_filename)[0]
    xslt_filename = f"{base_name}.xslt"
    
    print(f"--- Processing: {base_name} ---")
    
    # 1. Safe Read (Fixes the 0x92 Error)
    file_content, encoding_used = read_file_safely(csv_path)
    
    if file_content is None:
        print(f"CRITICAL ERROR: Could not read {csv_path} with any known encoding.")
        return

    print(f"   (Read success using encoding: {encoding_used})")

    try:
        # 2. Parse CSV from the loaded string content
        # We use io.StringIO to treat the string like a file
        f_obj = io.StringIO(file_content)
        reader = csv.DictReader(f_obj)
        
        # 3. Identify mapped columns
        target_attributes = {}
        if reader.fieldnames:
            for col in reader.fieldnames:
                if col.startswith('mapped_'):
                    attr_name = col.replace('mapped_', '')
                    target_attributes[col] = attr_name
        
        if not target_attributes:
            print(f"   Skipping: No columns starting with 'mapped_' found.")
            return

        print(f"   Mapping attributes: {list(target_attributes.values())}")

        # 4. Generate XSLT
        with open(xslt_filename, 'w', encoding='utf-8') as xslt_file:
            xslt_file.write(XSLT_HEADER)
            
            row_count = 0
            for row in reader:
                item_id = row.get('id', '').strip()
                
                if not item_id:
                    continue

                for csv_col, xml_attr in target_attributes.items():
                    new_value = row.get(csv_col, '').strip()
                    if new_value:
                        block = f"""
    <xsl:template match="*[@id='{item_id}']/@{xml_attr}">
        <xsl:attribute name="{xml_attr}">{new_value}</xsl:attribute>
    </xsl:template>"""
                        xslt_file.write(block)
                        row_count += 1

            xslt_file.write(XSLT_FOOTER)
            print(f"   SUCCESS: Generated '{xslt_filename}' with {row_count} updates.")

    except Exception as e:
        print(f"   ERROR processing {base_name}: {e}")

def main():
    csv_files = glob.glob(os.path.join(WORKING_DIR, "*.csv"))
    
    if not csv_files:
        print("No CSV files found in directory.")
        return

    for csv_file in csv_files:
        base_name = os.path.splitext(os.path.basename(csv_file))[0]
        expected_xml = os.path.join(WORKING_DIR, f"{base_name}.xml")
        
        if os.path.exists(expected_xml):
            process_file_pair(csv_file, os.path.basename(expected_xml))

if __name__ == "__main__":
    main()