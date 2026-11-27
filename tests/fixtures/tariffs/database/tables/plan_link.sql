-- Where each plan's own page is, kept by whoever edits the plans: a URL the procedures read from data.
CREATE TABLE billing.plan_link (
    plan_id int NOT NULL PRIMARY KEY,
    href nvarchar(400) NOT NULL);
