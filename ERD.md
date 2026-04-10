1.tools
id
tool_code
tool_name
tool_type
status
location
created_at

2.recipes
id
recipe_code
recipe_name
version
description
created_at

3.lots
id
lot_no
product_code
quantity
start_time
end_time
status
created_at

4.wafers
id
lot_id
wafer_no
status
created_at

5.process_runs
id
tool_id
recipe_id
lot_id
wafer_id
run_start_at
run_end_at
temperature
pressure
yield_rate
result_status
created_at

6.alarms
id
tool_id
process_run_id
alarm_code
alarm_level
message
triggered_at
cleared_at
status

7.defects
id
tool_id
lot_id
wafer_id
process_run_id
defect_code
defect_type
severity
x_coord
y_coord
detected_at
is_false_alarm

8.defect_images
id
defect_id
image_path
thumbnail_path
width
height
created_at

9.defect_reviews
id
defect_id
reviewer
review_result
review_comment
reviewed_at

10.documents
id
title
doc_type
version
source_path
uploaded_at

11.document_chunks
id
document_id
chunk_text
chunk_index
embedding_id
created_at

12.copilot_queries
id
query_text
related_alarm_id
related_defect_id
answer_text
source_refs
created_at

關聯

lots -> wafers
tools -> process_runs
recipes -> process_runs
lots -> process_runs
wafers -> process_runs
process_runs -> alarms
process_runs -> defects
defects -> defect_images
defects -> defect_reviews
documents -> document_chunks