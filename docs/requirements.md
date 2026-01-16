When I am in the main view
Then I see:
- Top: Navigation pane with tabs "Dashboard", "History", "Search", "Settings"
- Middle: an editable rich text task list for new tasks
    - Next to the title of the new tasks view: a button to move all the new tasks to "Uncategorized"
    - Next to the title of the new tasks view: a button to expand (and restore) the new task view to full screen (i.e. to hide other components but the navigation pane)
- Bottom: an editable rich text main task list in the middle

Given any task list
When I click a task as done
Then the task is strike through, text color is set to gray, and the timestamp of completing the task is saved.



Given any task list on the dashboard tab
When I have the focus on a task
Then there appears a floating ribbon below the task which has options:
- to categorize it to any of the existing categories
- to set (or unset) the recurrance of the task to be repeatable, daily, weekly (weekdays selectable), monthly (days of month selectable).
    - repeatable means that the task is categorized under "Repeatable" and any other options are also recorded there. Then when the day changes, or when the software is started, it is checked whether a new task should be created and populated for the next 4 weeks.

Given the new task list on the dashboard tab
When I look at it
Then I see the tasks under the category "New".

Given the main task list on the dashboard tab
When I look at it
Then I see the tasks under named categories, except "New" which is hidden in the main task list.

Given the categories of the tasks
Then the categories are:
- New, shown in the new tasks task list
- Uncategorized
- [category for each week, titled as "Week starting on YYYY-MM-DD", where the date is the date of the Monday for that week]
    - only show the next 4 weeks + any week in the past which still has tasks in it
- No date
- Repeatable
- Notes

Given the task list on the dashboard tab
When a day changes to the next, or the software is started
Then any tasks that have been marked as complete on a day earlier than today, shall be hidden from the main task list.



Given the history tab
When I navigate to the history tab
Then I can see:
- Top: a bar chart showing how many tasks were done each day. Shows only the last 180 days.
- Middle: a task view, which shows all the tasks that I had marked as complete earlier. Organized by date and the date is shown as the header for each group of tasks completed during that day.

Given the history tab
When I uncheck a task to be not done
Then the strike through is removed and the text color is set to black. The task is moved back to the dashboard tab.



Given the search tab
When I navigate to the search tab
Then I see:
- Top: a search bar and search button
- Middle: results of the search

Given the search tab
When I search a word
Then if the search was not yet done, show "". If the search was done but there was no results, show "No results". If there were results, show the matching tasks (from any view and regardless of status) in the results as read-only and highlight the searched text within the search results.
